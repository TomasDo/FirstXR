using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public interface IDentalCtSliceSource
    {
        bool HasVolume { get; }
        bool UsesPatientPlane { get; }
        int SliceIndex { get; }
        int SliceCount { get; }
        float SliceOffsetMm { get; }
        Texture2D CurrentSliceTexture { get; }
        string StatusMessage { get; }
        event Action SliceChanged;

        void SetSliceIndex(int sliceIndex);
        void StepSlice(int delta);
        void SetSliceOffsetMm(float offsetMm);
    }

    public readonly struct DicomOperationResult
    {
        public readonly bool Success;
        public readonly string Error;

        public DicomOperationResult(bool success, string error)
        {
            Success = success;
            Error = error ?? string.Empty;
        }
    }

    public readonly struct DicomAssetExpectation
    {
        public readonly string SopInstanceUid;
        public readonly uint FrameCount;
        public readonly uint OrderIndex;

        public DicomAssetExpectation(string sopInstanceUid, uint frameCount, uint orderIndex)
        {
            SopInstanceUid = sopInstanceUid ?? string.Empty;
            FrameCount = frameCount;
            OrderIndex = orderIndex;
        }

        public bool TryValidate(DicomDecodedFile decoded, out string error)
        {
            if (decoded == null)
            {
                error = "Decoded DICOM asset is missing.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(SopInstanceUid) || FrameCount == 0)
            {
                error = "DICOM manifest requires sop_instance_uid and a positive frame_count.";
                return false;
            }
            if (!string.Equals(decoded.SopInstanceUid, SopInstanceUid, StringComparison.Ordinal))
            {
                error = $"DICOM SOPInstanceUID mismatch. manifest={SopInstanceUid}, decoded={decoded.SopInstanceUid}";
                return false;
            }
            if ((ulong)decoded.Frames.Count != FrameCount)
            {
                error = $"DICOM frame count mismatch. manifest={FrameCount}, decoded={decoded.Frames.Count}";
                return false;
            }
            for (var index = 0; index < decoded.Frames.Count; index++)
            {
                var frame = decoded.Frames[index];
                if (!string.Equals(frame.SopInstanceUid, SopInstanceUid, StringComparison.Ordinal) ||
                    frame.FrameNumber != index + 1)
                {
                    error = "Decoded DICOM frame identity does not match the manifest SOP or frame order.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Runtime facade used by transport and HUD code. File verification and DICOM parsing finish
    /// before a frame enters the pending volume. Texture creation must be called on Unity's main thread.
    /// </summary>
    public sealed class DentalCtVolumeService : MonoBehaviour, IDentalCtSliceSource
    {
        [SerializeField]
        string m_StorageDirectoryName = "DentalNavigation/DicomV1";

        [SerializeField, Min(1)]
        int m_SliceCacheCapacity = 8;

        [SerializeField, Min(64)]
        int m_MaximumSliceResolution = 1024;

        static DentalCtVolumeService s_Instance;

        readonly object m_TransferScopeGate = new object();
        readonly List<DicomImageFrame> m_PendingFrames = new List<DicomImageFrame>();
        readonly HashSet<string> m_PendingSopInstanceUids = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<uint, string> m_PendingManifestOrder = new Dictionary<uint, string>();
        DentalDicomTransferStore m_TransferStore;
        IDicomDecoder m_Decoder;
        DicomVolume m_Volume;
        DicomSliceTextureCache m_TextureCache;
        DicomLatestSliceRenderQueue m_SliceRenderQueue;
        Texture2D m_CurrentSliceTexture;
        DicomVolume m_RequestedSliceVolume;
        DicomSliceRequest m_RequestedSlice;
        ulong m_LatestSliceRequestVersion;
        string m_RequestedSliceReadyStatus = string.Empty;
        long m_IngestGeneration;
        long m_CommitRequestVersion;
        long m_TransferGeneration;
        string m_ActiveTransferNamespace = string.Empty;
        int m_SliceIndex;
        bool m_UsesPatientPlane;
        Vector3 m_PlaneOriginMm;
        Vector3 m_PlaneNormal;
        Vector3 m_PlaneBuccalUp;
        float m_CustomSliceOffsetMm;
        float m_CustomSliceStepMm = 1f;
        string m_StatusMessage = "CT 未加载";

        public static DentalCtVolumeService Instance => s_Instance;
        public bool HasVolume => m_Volume != null;
        public int SliceIndex => HasVolume && !m_UsesPatientPlane ? m_SliceIndex : -1;
        public int SliceCount => HasVolume ? m_Volume.Depth : 0;
        public float SliceOffsetMm => !HasVolume ? 0f :
            m_UsesPatientPlane ? m_CustomSliceOffsetMm : (float)(m_SliceIndex * m_Volume.SliceSpacingMm);
        public Texture2D CurrentSliceTexture => m_CurrentSliceTexture;
        public string StatusMessage => m_StatusMessage;
        public DicomVolume CurrentVolume => m_Volume;
        public bool UsesPatientPlane => HasVolume && m_UsesPatientPlane;
        public long ActiveTransferGeneration => Volatile.Read(ref m_TransferGeneration);
        public string ActiveTransferNamespace
        {
            get
            {
                lock (m_TransferScopeGate)
                    return m_ActiveTransferNamespace;
            }
        }

        public event Action SliceChanged;

        public static DentalCtVolumeService EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = FindObjectOfType<DentalCtVolumeService>();
            if (existing != null)
            {
                s_Instance = existing;
                return existing;
            }

            var obj = new GameObject("Dental CT Volume Service");
            return obj.AddComponent<DentalCtVolumeService>();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
            s_Instance = this;
            EnsureDependencies();
            if (GetComponent<DentalCtSliceCoordinator>() == null)
                gameObject.AddComponent<DentalCtSliceCoordinator>();
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
            Interlocked.Increment(ref m_IngestGeneration);
            Interlocked.Increment(ref m_CommitRequestVersion);
            m_SliceRenderQueue?.Dispose();
            m_TextureCache?.Dispose();
        }

        void Update()
        {
            if (m_SliceRenderQueue == null || !m_SliceRenderQueue.TryTakeLatest(out var result) ||
                result.RequestVersion != m_LatestSliceRequestVersion ||
                !ReferenceEquals(result.Volume, m_Volume) ||
                !ReferenceEquals(result.Volume, m_RequestedSliceVolume) ||
                !result.Request.Equals(m_RequestedSlice))
                return;

            if (!result.Success)
            {
                m_StatusMessage = "CT 切片生成失败：" + result.Error;
                RaiseSliceChanged();
                return;
            }

            try
            {
                // Update runs on Unity's main thread. This is the only service path that creates
                // and uploads a Texture2D for a newly sampled slice.
                m_CurrentSliceTexture = m_TextureCache.GetOrCreateFromPixels(
                    result.Volume,
                    result.Request,
                    result.Pixels);
                m_StatusMessage = m_RequestedSliceReadyStatus;
            }
            catch (Exception exception)
            {
                m_StatusMessage = "CT 纹理创建失败：" + exception.Message;
            }
            RaiseSliceChanged();
        }

        /// <summary>Allows tests or a future DCMTK adapter to replace storage and decoding before ingest.</summary>
        public void Configure(string storageDirectory, IDicomDecoder decoder = null)
        {
            if (string.IsNullOrWhiteSpace(storageDirectory))
                throw new ArgumentException("A CT storage directory is required.", nameof(storageDirectory));
            m_TransferStore = new DentalDicomTransferStore(storageDirectory);
            m_Decoder = decoder ?? CreateDefaultDecoder();
            InvalidateAsyncIngest();
            lock (m_TransferScopeGate)
                m_ActiveTransferNamespace = string.Empty;
            ActivateTransferScope(DicomTransferFileDescriptor.DefaultTransferNamespace);
        }

        /// <summary>
        /// Activates a stable namespace for one manifest identity and returns its runtime generation.
        /// Replaying the same manifest preserves resume state; switching namespaces invalidates all
        /// outstanding decode/commit work. Call this on Unity's main thread before BeginAsset.
        /// </summary>
        public long ActivateTransferScope(string transferNamespace)
        {
            EnsureDependencies();
            if (string.IsNullOrWhiteSpace(transferNamespace) || transferNamespace.Length > 256)
                throw new ArgumentException("A DICOM transfer namespace of at most 256 characters is required.", nameof(transferNamespace));

            long generation;
            lock (m_TransferScopeGate)
            {
                if (string.Equals(m_ActiveTransferNamespace, transferNamespace, StringComparison.Ordinal) &&
                    m_TransferGeneration != 0)
                    return m_TransferGeneration;

                m_ActiveTransferNamespace = transferNamespace;
                generation = unchecked(m_TransferGeneration + 1);
                if (generation == 0)
                    generation = 1;
                Volatile.Write(ref m_TransferGeneration, generation);
            }

            InvalidateAsyncIngest();
            Interlocked.Increment(ref m_CommitRequestVersion);
            m_PendingFrames.Clear();
            ClearPendingAssetIdentities();
            return generation;
        }

        /// <summary>
        /// Immediately prevents every outstanding operation for the previous navigation context
        /// from writing, decoding, or committing, then removes its volume from the display.
        /// Call on Unity's main thread when a different NavigationContext is accepted.
        /// </summary>
        public void InvalidateTransferScopeAndClearVolume()
        {
            lock (m_TransferScopeGate)
            {
                m_ActiveTransferNamespace = string.Empty;
                var generation = unchecked(m_TransferGeneration + 1);
                if (generation == 0)
                    generation = 1;
                Volatile.Write(ref m_TransferGeneration, generation);
            }
            ClearVolume();
        }

        public bool BeginAsset(DicomTransferFileDescriptor descriptor, out string error)
        {
            if (descriptor == null)
            {
                error = "DICOM asset descriptor is missing.";
                return false;
            }
            var generation = ActivateTransferScope(descriptor.TransferNamespace);
            return BeginAsset(generation, descriptor.TransferNamespace, descriptor, out error);
        }

        public bool BeginAsset(
            long transferGeneration,
            string transferNamespace,
            DicomTransferFileDescriptor descriptor,
            out string error)
        {
            EnsureDependencies();
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out error))
                return false;
            if (descriptor == null ||
                !string.Equals(descriptor.TransferNamespace, transferNamespace, StringComparison.Ordinal))
            {
                error = "DICOM descriptor does not belong to the active transfer namespace.";
                return false;
            }

            var accepted = m_TransferStore.BeginAsset(descriptor, out error);
            m_StatusMessage = accepted ? "CT 接收中" : "CT 接收失败：" + error;
            RaiseSliceChanged();
            return accepted;
        }

        public bool WriteAssetChunk(string assetId, long offset, byte[] data, out string error)
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return WriteAssetChunk(transferGeneration, transferNamespace, assetId, offset, data, out error);
        }

        public bool WriteAssetChunk(
            long transferGeneration,
            string transferNamespace,
            string assetId,
            long offset,
            byte[] data,
            out string error)
        {
            EnsureDependencies();
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out error))
                return false;
            var accepted = m_TransferStore.WriteChunk(transferNamespace, assetId, offset, data, out error);
            if (!accepted)
            {
                m_StatusMessage = "CT 分块错误：" + error;
                RaiseSliceChanged();
            }
            return accepted;
        }

        /// <summary>
        /// Persists one chunk without touching Unity objects or raising events. BeginAsset must
        /// already have run on the main thread. This method is safe to call from the asset worker
        /// so transport ACKs can mean that bytes were flushed to the resumable store.
        /// </summary>
        public DicomOperationResult WriteAssetChunkBackground(string assetId, long offset, byte[] data)
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return WriteAssetChunkBackground(transferGeneration, transferNamespace, assetId, offset, data);
        }

        public DicomOperationResult WriteAssetChunkBackground(
            long transferGeneration,
            string transferNamespace,
            string assetId,
            long offset,
            byte[] data)
        {
            var store = m_TransferStore;
            if (store == null)
                return new DicomOperationResult(false, "The DICOM asset store has not been initialized on the main thread.");
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out var scopeError))
                return new DicomOperationResult(false, scopeError);

            return store.WriteChunk(transferNamespace, assetId, offset, data, out var error)
                ? new DicomOperationResult(true, string.Empty)
                : new DicomOperationResult(false, error);
        }

        public bool TryGetAssetProgress(string assetId, out DicomTransferProgress progress)
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return TryGetAssetProgress(transferGeneration, transferNamespace, assetId, out progress);
        }

        public bool TryGetAssetProgress(
            long transferGeneration,
            string transferNamespace,
            string assetId,
            out DicomTransferProgress progress)
        {
            EnsureDependencies();
            progress = null;
            return TryValidateTransferScope(transferGeneration, transferNamespace, out _) &&
                   m_TransferStore.TryGetProgress(transferNamespace, assetId, out progress);
        }

        public bool CompleteAsset(string assetId, out string error)
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return CompleteAsset(transferGeneration, transferNamespace, assetId, out error);
        }

        public bool CompleteAsset(
            long transferGeneration,
            string transferNamespace,
            string assetId,
            out string error)
        {
            EnsureDependencies();
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out error))
                return false;
            if (!m_TransferStore.TryCompleteAsset(transferNamespace, assetId, out var completedPath, out error))
            {
                m_StatusMessage = "CT 校验失败：" + error;
                RaiseSliceChanged();
                return false;
            }

            if (!m_Decoder.TryDecode(completedPath, out var decoded, out error))
            {
                m_StatusMessage = "CT 解码失败：" + error;
                RaiseSliceChanged();
                return false;
            }

            if (!TryAddDecodedAsset(assetId, decoded, null, out error))
            {
                m_StatusMessage = "CT 实例校验失败：" + error;
                RaiseSliceChanged();
                return false;
            }
            m_StatusMessage = $"CT 已校验：{m_PendingFrames.Count} 层待构建";
            RaiseSliceChanged();
            return true;
        }

        /// <summary>
        /// Verifies SHA-256 and decodes pixels on a worker thread. Await this from Unity's main
        /// synchronization context so the final status/event update also occurs on the main thread.
        /// </summary>
        public async Task<DicomOperationResult> CompleteAssetAsync(string assetId)
        {
            return await CompleteAssetAsync(assetId, null);
        }

        public async Task<DicomOperationResult> CompleteAssetAsync(
            string assetId,
            DicomAssetExpectation? expectation)
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return await CompleteAssetAsync(
                transferGeneration,
                transferNamespace,
                assetId,
                expectation);
        }

        public async Task<DicomOperationResult> CompleteAssetAsync(
            long transferGeneration,
            string transferNamespace,
            string assetId,
            DicomAssetExpectation? expectation)
        {
            EnsureDependencies();
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out var scopeError))
                return new DicomOperationResult(false, scopeError);
            var store = m_TransferStore;
            var decoder = m_Decoder;
            var generation = Volatile.Read(ref m_IngestGeneration);
            var context = CaptureContextIdentity();
            var result = await Task.Run(() => DecodeCompletedAsset(store, decoder, transferNamespace, assetId));
            if (!IsCurrentIngest(generation, context) ||
                !IsCurrentTransferScope(transferGeneration, transferNamespace))
                return new DicomOperationResult(false, "DICOM decode result was discarded after the transfer or navigation context changed.");
            if (!result.Success)
            {
                m_StatusMessage = "CT 校验或解码失败：" + result.Error;
                RaiseSliceChanged();
                return new DicomOperationResult(false, result.Error);
            }

            if (!TryAddDecodedAsset(assetId, result.Decoded, expectation, out var validationError))
            {
                m_StatusMessage = "CT 实例校验失败：" + validationError;
                RaiseSliceChanged();
                return new DicomOperationResult(false, validationError);
            }
            m_StatusMessage = $"CT 已校验：{m_PendingFrames.Count} 层待构建";
            RaiseSliceChanged();
            return new DicomOperationResult(true, string.Empty);
        }

        public bool LoadVerifiedDicomFile(string filePath, out string error)
        {
            EnsureDependencies();
            if (!m_Decoder.TryDecode(filePath, out var decoded, out error))
            {
                m_StatusMessage = "CT 解码失败：" + error;
                RaiseSliceChanged();
                return false;
            }
            if (!TryAddDecodedAsset(filePath, decoded, null, out error))
            {
                m_StatusMessage = "CT 实例校验失败：" + error;
                RaiseSliceChanged();
                return false;
            }
            m_StatusMessage = $"CT 已解码：{m_PendingFrames.Count} 层待构建";
            RaiseSliceChanged();
            return true;
        }

        public bool CommitVolume(out string error)
        {
            EnsureDependencies();
            Interlocked.Increment(ref m_CommitRequestVersion);
            if (!DicomVolume.TryCreate(m_PendingFrames, out var volume, out error))
            {
                m_StatusMessage = "CT 体数据构建失败：" + error;
                RaiseSliceChanged();
                return false;
            }

            ResetSlicePipelineForNewVolume();
            m_Volume = volume;
            m_SliceIndex = Mathf.Clamp(volume.Depth / 2, 0, volume.Depth - 1);
            m_UsesPatientPlane = false;
            m_PendingFrames.Clear();
            ClearPendingAssetIdentities();
            RefreshNativeSlice();
            return true;
        }

        /// <summary>
        /// Builds and resamples the contiguous volume on a worker thread. Only the final cached
        /// Texture2D is created after the await resumes on Unity's main thread.
        /// </summary>
        public async Task<DicomOperationResult> CommitVolumeAsync()
        {
            CaptureActiveTransfer(out var transferNamespace, out var transferGeneration);
            return await CommitVolumeAsync(transferGeneration, transferNamespace);
        }

        public async Task<DicomOperationResult> CommitVolumeAsync(
            long transferGeneration,
            string transferNamespace)
        {
            EnsureDependencies();
            if (!TryValidateTransferScope(transferGeneration, transferNamespace, out var scopeError))
                return new DicomOperationResult(false, scopeError);
            var frames = m_PendingFrames.ToArray();
            var generation = Volatile.Read(ref m_IngestGeneration);
            var commitRequestVersion = Interlocked.Increment(ref m_CommitRequestVersion);
            var context = CaptureContextIdentity();
            var result = await Task.Run(() => BuildVolume(frames));
            if (!IsCurrentIngest(generation, context) ||
                !IsCurrentTransferScope(transferGeneration, transferNamespace) ||
                commitRequestVersion != Volatile.Read(ref m_CommitRequestVersion))
                return new DicomOperationResult(false, "DICOM volume result was discarded after a newer transfer, context, or commit request.");
            if (!result.Success)
            {
                m_StatusMessage = "CT 体数据构建失败：" + result.Error;
                RaiseSliceChanged();
                return new DicomOperationResult(false, result.Error);
            }

            ResetSlicePipelineForNewVolume();
            m_Volume = result.Volume;
            m_SliceIndex = Mathf.Clamp(m_Volume.Depth / 2, 0, m_Volume.Depth - 1);
            m_UsesPatientPlane = false;
            m_PendingFrames.Clear();
            ClearPendingAssetIdentities();
            RefreshNativeSlice();
            return new DicomOperationResult(true, string.Empty);
        }

        public void SetSliceIndex(int sliceIndex)
        {
            if (!HasVolume)
                return;
            var clamped = Mathf.Clamp(sliceIndex, 0, m_Volume.Depth - 1);
            if (!m_UsesPatientPlane && clamped == m_SliceIndex && m_CurrentSliceTexture != null)
                return;
            m_UsesPatientPlane = false;
            m_SliceIndex = clamped;
            RefreshNativeSlice();
        }

        public void StepSlice(int delta)
        {
            if (!HasVolume || delta == 0)
                return;
            if (m_UsesPatientPlane)
            {
                SetPatientPlane(
                    m_PlaneOriginMm,
                    m_PlaneNormal,
                    m_PlaneBuccalUp,
                    m_CustomSliceOffsetMm + delta * m_CustomSliceStepMm,
                    out _);
                return;
            }
            var requested = (long)m_SliceIndex + delta;
            SetSliceIndex((int)Math.Max(0, Math.Min(m_Volume.Depth - 1L, requested)));
        }

        public void SetSliceOffsetMm(float offsetMm)
        {
            if (!HasVolume || float.IsNaN(offsetMm) || float.IsInfinity(offsetMm))
                return;
            if (m_UsesPatientPlane)
            {
                SetPatientPlane(m_PlaneOriginMm, m_PlaneNormal, m_PlaneBuccalUp, offsetMm, out _);
                return;
            }
            SetSliceIndex((int)Math.Round(offsetMm / m_Volume.SliceSpacingMm, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Sets a patient-space physical slice. Contract: buccalUp points toward the top label;
        /// Cross(normal, buccalUp) points toward the screen-left mesial label. The origin and
        /// offset are millimetres in the same patient coordinate frame as DICOM geometry.
        /// </summary>
        public bool SetPatientPlane(
            Vector3 originMm,
            Vector3 normal,
            Vector3 buccalUp,
            float offsetMm,
            out string error)
        {
            error = string.Empty;
            if (!HasVolume)
            {
                error = "CT 体数据尚未构建。";
                return false;
            }
            if (!IsFinite(originMm) || !IsFinite(normal) || !IsFinite(buccalUp) ||
                float.IsNaN(offsetMm) || float.IsInfinity(offsetMm))
            {
                error = "切片平面包含无效数值。";
                return false;
            }
            if (normal.sqrMagnitude < 1e-8f || buccalUp.sqrMagnitude < 1e-8f)
            {
                error = "切片法向和颊侧向上轴不能为空。";
                return false;
            }
            if (!DicomPatientPlaneFactory.TryCreate(
                    m_Volume,
                    ToDicom(originMm),
                    ToDicom(normal),
                    ToDicom(buccalUp),
                    offsetMm,
                    Mathf.Max(64, m_MaximumSliceResolution),
                    out var patientPlane,
                    out var factoryError))
            {
                error = "切片平面无效：" + factoryError;
                return false;
            }

            normal.Normalize();
            buccalUp = Vector3.ProjectOnPlane(buccalUp, normal).normalized;
            m_UsesPatientPlane = true;
            m_PlaneOriginMm = originMm;
            m_PlaneNormal = normal;
            m_PlaneBuccalUp = buccalUp;
            m_CustomSliceOffsetMm = offsetMm;
            m_CustomSliceStepMm = (float)patientPlane.StepMm;
            ScheduleSlice(patientPlane.Request, $"CT 规划切片 · 偏移 {offsetMm:0.0} mm");
            return true;
        }

        /// <summary>Renders an arbitrary patient-space plane, for example one perpendicular to a plan axis.</summary>
        public void SetSlicePlane(DicomSliceRequest request)
        {
            if (!HasVolume)
                return;
            ScheduleSlice(request, "CT 自定义切片");
        }

        public void ClearVolume()
        {
            EnsureDependencies();
            InvalidateAsyncIngest();
            Interlocked.Increment(ref m_CommitRequestVersion);
            m_SliceRenderQueue.Invalidate();
            m_TextureCache.Clear();
            m_PendingFrames.Clear();
            ClearPendingAssetIdentities();
            m_Volume = null;
            m_CurrentSliceTexture = null;
            m_SliceIndex = 0;
            m_UsesPatientPlane = false;
            m_StatusMessage = "CT 未加载";
            RaiseSliceChanged();
        }

        void RefreshNativeSlice()
        {
            if (!HasVolume)
                return;
            var request = DicomSliceRequest.Native(m_Volume, m_SliceIndex);
            ScheduleSlice(request, $"CT 第 {m_SliceIndex + 1}/{m_Volume.Depth} 层");
        }

        void EnsureDependencies()
        {
            if (m_Decoder == null)
                m_Decoder = CreateDefaultDecoder();
            if (m_TextureCache == null)
                m_TextureCache = new DicomSliceTextureCache(Mathf.Max(1, m_SliceCacheCapacity));
            if (m_SliceRenderQueue == null)
                m_SliceRenderQueue = new DicomLatestSliceRenderQueue(Mathf.Max(1, m_SliceCacheCapacity));
            if (m_TransferStore == null)
            {
                var relativeDirectory = string.IsNullOrWhiteSpace(m_StorageDirectoryName)
                    ? "DentalNavigation/DicomV1"
                    : m_StorageDirectoryName;
                m_TransferStore = new DentalDicomTransferStore(Path.Combine(Application.persistentDataPath, relativeDirectory));
            }
        }

        void RaiseSliceChanged()
        {
            var handler = SliceChanged;
            handler?.Invoke();
        }

        void ScheduleSlice(DicomSliceRequest request, string readyStatus)
        {
            EnsureDependencies();
            m_RequestedSliceVolume = m_Volume;
            m_RequestedSlice = request;
            m_RequestedSliceReadyStatus = readyStatus ?? "CT 切片";
            if (m_TextureCache.TryGet(m_Volume, request, out var cachedTexture))
            {
                m_LatestSliceRequestVersion = m_SliceRenderQueue.Invalidate();
                m_CurrentSliceTexture = cachedTexture;
                m_StatusMessage = m_RequestedSliceReadyStatus;
            }
            else
            {
                m_LatestSliceRequestVersion = m_SliceRenderQueue.Request(m_Volume, request);
                m_StatusMessage = m_RequestedSliceReadyStatus + " · 生成中";
            }
            RaiseSliceChanged();
        }

        void ResetSlicePipelineForNewVolume()
        {
            m_SliceRenderQueue.Invalidate();
            m_TextureCache.Clear();
            m_CurrentSliceTexture = null;
            m_RequestedSliceVolume = null;
            m_LatestSliceRequestVersion = 0;
            m_RequestedSliceReadyStatus = string.Empty;
        }

        void InvalidateAsyncIngest()
        {
            Interlocked.Increment(ref m_IngestGeneration);
        }

        bool TryAddDecodedAsset(
            string assetId,
            DicomDecodedFile decoded,
            DicomAssetExpectation? expectation,
            out string error)
        {
            error = string.Empty;
            if (decoded == null || decoded.Frames == null || decoded.Frames.Count == 0)
            {
                error = "Decoded DICOM asset contains no image frames.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(decoded.SopInstanceUid))
            {
                error = "Decoded DICOM asset has no SOPInstanceUID.";
                return false;
            }
            if (expectation.HasValue && !expectation.Value.TryValidate(decoded, out error))
                return false;
            if (m_PendingSopInstanceUids.Contains(decoded.SopInstanceUid))
            {
                error = "A DICOM asset with the same SOPInstanceUID is already pending.";
                return false;
            }
            if (expectation.HasValue &&
                m_PendingManifestOrder.TryGetValue(expectation.Value.OrderIndex, out var existingAsset))
            {
                error = $"DICOM order_index {expectation.Value.OrderIndex} is duplicated by '{existingAsset}' and '{assetId}'.";
                return false;
            }

            m_PendingFrames.AddRange(decoded.Frames);
            m_PendingSopInstanceUids.Add(decoded.SopInstanceUid);
            if (expectation.HasValue)
                m_PendingManifestOrder.Add(expectation.Value.OrderIndex, assetId ?? string.Empty);
            return true;
        }

        void ClearPendingAssetIdentities()
        {
            m_PendingSopInstanceUids.Clear();
            m_PendingManifestOrder.Clear();
        }

        bool IsCurrentIngest(long generation, NavigationContextIdentity context)
        {
            return generation == Volatile.Read(ref m_IngestGeneration) &&
                   context.Equals(CaptureContextIdentity());
        }

        bool TryValidateTransferScope(
            long transferGeneration,
            string transferNamespace,
            out string error)
        {
            if (!IsCurrentTransferScope(transferGeneration, transferNamespace))
            {
                error = "DICOM transfer namespace or generation is stale.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        bool IsCurrentTransferScope(long transferGeneration, string transferNamespace)
        {
            lock (m_TransferScopeGate)
            {
                return transferGeneration != 0 &&
                       transferGeneration == m_TransferGeneration &&
                       string.Equals(m_ActiveTransferNamespace, transferNamespace, StringComparison.Ordinal);
            }
        }

        void CaptureActiveTransfer(out string transferNamespace, out long transferGeneration)
        {
            lock (m_TransferScopeGate)
            {
                transferNamespace = m_ActiveTransferNamespace;
                transferGeneration = m_TransferGeneration;
            }
        }

        static NavigationContextIdentity CaptureContextIdentity()
        {
            var state = DentalNavigationState.Instance;
            if (state == null)
                return default;
            var snapshot = state.Capture(0f);
            return snapshot.HasContext
                ? new NavigationContextIdentity(
                    true,
                    snapshot.Context.SessionId,
                    snapshot.Context.ContextVersion,
                    snapshot.Context.CtId)
                : default;
        }

        internal void ReportSliceStatus(string message)
        {
            m_StatusMessage = string.IsNullOrEmpty(message) ? "CT 切片状态未知" : message;
            RaiseSliceChanged();
        }

        static DicomAssetDecodeResult DecodeCompletedAsset(
            DentalDicomTransferStore store,
            IDicomDecoder decoder,
            string transferNamespace,
            string assetId)
        {
            if (!store.TryCompleteAsset(transferNamespace, assetId, out var completedPath, out var error))
                return new DicomAssetDecodeResult(null, error);
            return decoder.TryDecode(completedPath, out var decoded, out error)
                ? new DicomAssetDecodeResult(decoded, string.Empty)
                : new DicomAssetDecodeResult(null, error);
        }

        static DicomVolumeBuildResult BuildVolume(DicomImageFrame[] frames)
        {
            return DicomVolume.TryCreate(frames, out var volume, out var error)
                ? new DicomVolumeBuildResult(volume, string.Empty)
                : new DicomVolumeBuildResult(null, error);
        }

        static DicomVector3d ToDicom(Vector3 value) => new DicomVector3d(value.x, value.y, value.z);

        static IDicomDecoder CreateDefaultDecoder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var nativeDecoder = new DcmtkAndroidDicomDecoder();
            if (nativeDecoder.IsAvailable)
                return nativeDecoder;
#endif
            return new ExplicitVrLittleEndianDicomDecoder();
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        readonly struct DicomAssetDecodeResult
        {
            public readonly DicomDecodedFile Decoded;
            public readonly string Error;
            public bool Success => Decoded != null;

            public DicomAssetDecodeResult(DicomDecodedFile decoded, string error)
            {
                Decoded = decoded;
                Error = error ?? string.Empty;
            }
        }

        readonly struct DicomVolumeBuildResult
        {
            public readonly DicomVolume Volume;
            public readonly string Error;
            public bool Success => Volume != null;

            public DicomVolumeBuildResult(DicomVolume volume, string error)
            {
                Volume = volume;
                Error = error ?? string.Empty;
            }
        }

        readonly struct NavigationContextIdentity : IEquatable<NavigationContextIdentity>
        {
            readonly bool m_HasContext;
            readonly string m_SessionId;
            readonly ulong m_ContextVersion;
            readonly string m_CtId;

            public NavigationContextIdentity(bool hasContext, string sessionId, ulong contextVersion, string ctId)
            {
                m_HasContext = hasContext;
                m_SessionId = sessionId ?? string.Empty;
                m_ContextVersion = contextVersion;
                m_CtId = ctId ?? string.Empty;
            }

            public bool Equals(NavigationContextIdentity other)
            {
                return m_HasContext == other.m_HasContext &&
                       string.Equals(m_SessionId, other.m_SessionId, StringComparison.Ordinal) &&
                       m_ContextVersion == other.m_ContextVersion &&
                       string.Equals(m_CtId, other.m_CtId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is NavigationContextIdentity other && Equals(other);
            public override int GetHashCode() => (m_HasContext, m_SessionId, m_ContextVersion, m_CtId).GetHashCode();
        }
    }
}
