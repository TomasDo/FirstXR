using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public enum DentalSliceExecutionDisposition
    {
        None = 0,
        PendingVolume = 1,
        Applied = 2,
        Rejected = 3,
    }

    /// <summary>
    /// Applies the authoritative slice state to the decoded CT volume. It retries a pending state
    /// when a new volume is committed, but never reapplies merely because a local slice change fired.
    /// </summary>
    public sealed class DentalCtSliceCoordinator : MonoBehaviour
    {
        static DentalCtSliceCoordinator s_Instance;

        DentalNavigationState m_NavigationState;
        DentalCtVolumeService m_CtService;
        DentalNavigationContext m_Context;
        DentalSliceState m_PendingSliceState;
        bool m_HasContext;
        bool m_HasPendingSliceState;
        bool m_IsApplying;
        DicomVolume m_LastAttemptedVolume;
        ulong m_LastAttemptedContextVersion;
        ulong m_LastAttemptedControlVersion;
        bool m_LastAttemptUsedControlState;
        string m_LastWarning = string.Empty;
        string m_LastExecutionSessionId = string.Empty;
        ulong m_LastExecutionContextVersion;
        ulong m_LastExecutionControlVersion;
        DentalSliceExecutionDisposition m_LastExecutionDisposition;
        string m_LastExecutionError = string.Empty;

        public static DentalCtSliceCoordinator Instance => s_Instance;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
            s_Instance = this;
            m_CtService = GetComponent<DentalCtVolumeService>();
            if (m_CtService == null)
                m_CtService = DentalCtVolumeService.EnsureInstance();
            m_NavigationState = DentalNavigationState.EnsureInstance();
        }

        void OnEnable()
        {
            Bind();
            CaptureCurrentState();
            TryApplyBestSlice();
        }

        void OnDisable()
        {
            if (m_NavigationState != null)
            {
                m_NavigationState.ContextChanged -= OnContextChanged;
                m_NavigationState.SliceStateChanged -= OnSliceStateChanged;
            }
            if (m_CtService != null)
                m_CtService.SliceChanged -= OnCtSliceChanged;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        /// <summary>
        /// Executes a remotely supplied state before the navigation state commits its version.
        /// A missing CT is an accepted pending state; invalid volume/range/geometry is rejected.
        /// </summary>
        public bool TryExecuteControl(
            DentalSliceState sliceState,
            out DentalSliceExecutionDisposition disposition,
            out string error)
        {
            Bind();
            if (!m_HasContext)
                CaptureCurrentState();
            if (!m_HasContext
                || sliceState.ContextVersion != m_Context.ContextVersion
                || !string.Equals(sliceState.SessionId, m_Context.SessionId, StringComparison.Ordinal))
            {
                disposition = DentalSliceExecutionDisposition.Rejected;
                error = "切片指令与当前导航上下文不一致。";
                return false;
            }

            m_PendingSliceState = sliceState;
            m_HasPendingSliceState = true;
            TryApplyBestSlice();
            if (!string.Equals(m_LastExecutionSessionId, sliceState.SessionId, StringComparison.Ordinal)
                || m_LastExecutionContextVersion != sliceState.ContextVersion
                || m_LastExecutionControlVersion != sliceState.ControlVersion)
            {
                disposition = DentalSliceExecutionDisposition.Rejected;
                error = "切片指令未产生可核对的执行结果。";
                return false;
            }
            disposition = m_LastExecutionDisposition;
            error = m_LastExecutionError;
            return disposition == DentalSliceExecutionDisposition.Applied
                || disposition == DentalSliceExecutionDisposition.PendingVolume;
        }

        void Bind()
        {
            if (m_NavigationState == null)
                m_NavigationState = DentalNavigationState.EnsureInstance();
            if (m_CtService == null)
                m_CtService = DentalCtVolumeService.EnsureInstance();

            m_NavigationState.ContextChanged -= OnContextChanged;
            m_NavigationState.ContextChanged += OnContextChanged;
            m_NavigationState.SliceStateChanged -= OnSliceStateChanged;
            m_NavigationState.SliceStateChanged += OnSliceStateChanged;
            m_CtService.SliceChanged -= OnCtSliceChanged;
            m_CtService.SliceChanged += OnCtSliceChanged;
        }

        void CaptureCurrentState()
        {
            var snapshot = m_NavigationState.Capture(Time.realtimeSinceStartup);
            m_HasContext = snapshot.HasContext;
            if (snapshot.HasContext)
                m_Context = snapshot.Context;
            m_HasPendingSliceState = snapshot.HasSliceState;
            if (snapshot.HasSliceState)
                m_PendingSliceState = snapshot.SliceState;
        }

        void OnContextChanged(DentalNavigationContext context)
        {
            m_Context = context;
            m_HasContext = true;
            m_HasPendingSliceState = false;
            m_LastAttemptedVolume = null;
            m_LastAttemptedContextVersion = 0;
            m_LastAttemptedControlVersion = 0;
            m_LastAttemptUsedControlState = false;
            m_LastExecutionDisposition = DentalSliceExecutionDisposition.None;
            m_LastExecutionError = string.Empty;
            TryApplyBestSlice();
        }

        void OnSliceStateChanged(DentalSliceState sliceState)
        {
            if (!m_HasContext ||
                sliceState.ContextVersion != m_Context.ContextVersion ||
                !string.Equals(sliceState.SessionId, m_Context.SessionId, StringComparison.Ordinal))
                return;

            m_PendingSliceState = sliceState;
            m_HasPendingSliceState = true;
            TryApplyBestSlice();
        }

        void OnCtSliceChanged()
        {
            if (!m_IsApplying)
                TryApplyBestSlice();
        }

        void TryApplyBestSlice()
        {
            if (m_IsApplying || !m_HasContext || m_CtService == null)
                return;

            if (!m_CtService.HasVolume)
            {
                if (m_HasPendingSliceState)
                    RecordControlResult(DentalSliceExecutionDisposition.PendingVolume, string.Empty);
                return;
            }

            var volume = m_CtService.CurrentVolume;
            var volumeChanged = !ReferenceEquals(volume, m_LastAttemptedVolume);
            var contextChanged = m_LastAttemptedContextVersion != m_Context.ContextVersion;

            if (m_HasPendingSliceState)
            {
                if (!volumeChanged && !contextChanged && m_LastAttemptUsedControlState &&
                    m_PendingSliceState.ControlVersion <= m_LastAttemptedControlVersion)
                    return;
                AttemptApplyControlState(volume);
                return;
            }

            if (!volumeChanged && !contextChanged && !m_LastAttemptUsedControlState)
                return;
            AttemptApplyDefaultPlane(volume);
        }

        void AttemptApplyControlState(DicomVolume volume)
        {
            m_LastAttemptedVolume = volume;
            m_LastAttemptedContextVersion = m_Context.ContextVersion;
            m_LastAttemptedControlVersion = m_PendingSliceState.ControlVersion;
            m_LastAttemptUsedControlState = true;

            if (!string.IsNullOrEmpty(m_PendingSliceState.VolumeId) &&
                !string.IsNullOrEmpty(m_Context.CtId) &&
                !string.Equals(m_PendingSliceState.VolumeId, m_Context.CtId, StringComparison.Ordinal))
            {
                RejectControl("导航端切片引用的 CT 与当前上下文不一致。");
                return;
            }

            m_IsApplying = true;
            try
            {
                if (!m_PendingSliceState.HasPlane)
                {
                    if (m_PendingSliceState.SliceIndex < 0 || m_PendingSliceState.SliceIndex >= m_CtService.SliceCount)
                    {
                        RejectControl("导航端切片序号超出当前 CT 范围。");
                        return;
                    }
                    m_CtService.SetSliceIndex(m_PendingSliceState.SliceIndex);
                    ClearWarning();
                    RecordControlResult(DentalSliceExecutionDisposition.Applied, string.Empty);
                    return;
                }

                var planeFrame = m_PendingSliceState.FrameOfReferenceUid;
                if (!string.IsNullOrEmpty(planeFrame) &&
                    !string.Equals(planeFrame, volume.FrameOfReferenceUid, StringComparison.Ordinal) &&
                    !string.Equals(planeFrame, m_Context.CoordinateFrameId, StringComparison.Ordinal))
                {
                    RejectControl("导航端切片引用了未知的患者坐标系。");
                    return;
                }
                var coordinatesAreDicom = !string.IsNullOrEmpty(volume.FrameOfReferenceUid) &&
                                          (string.Equals(planeFrame, volume.FrameOfReferenceUid, StringComparison.Ordinal) ||
                                           (string.IsNullOrEmpty(planeFrame) &&
                                            string.Equals(m_Context.CoordinateFrameId, volume.FrameOfReferenceUid, StringComparison.Ordinal)));
                if (!TryConvertPlaneToDicom(
                        coordinatesAreDicom,
                        m_PendingSliceState.PlaneOriginMm,
                        m_PendingSliceState.PlaneNormal,
                        m_PendingSliceState.PlaneUp,
                        out var origin,
                        out var normal,
                        out var up,
                        out var error))
                {
                    RejectControl(error);
                    return;
                }
                if (!TryValidateMesialLeft(normal, up, out error))
                {
                    RejectControl(error);
                    return;
                }
                if (!m_CtService.SetPatientPlane(origin, normal, up, m_PendingSliceState.OffsetMm, out error))
                {
                    RejectControl(error);
                    return;
                }
                ClearWarning();
                RecordControlResult(DentalSliceExecutionDisposition.Applied, string.Empty);
            }
            finally
            {
                m_IsApplying = false;
            }
        }

        void AttemptApplyDefaultPlane(DicomVolume volume)
        {
            m_LastAttemptedVolume = volume;
            m_LastAttemptedContextVersion = m_Context.ContextVersion;
            m_LastAttemptedControlVersion = 0;
            m_LastAttemptUsedControlState = false;
            var coordinatesAreDicom = !string.IsNullOrEmpty(volume.FrameOfReferenceUid) &&
                                      string.Equals(m_Context.CoordinateFrameId, volume.FrameOfReferenceUid, StringComparison.Ordinal);

            if (!TryConvertPlaneToDicom(
                    coordinatesAreDicom,
                    m_Context.PlanEntryMm,
                    m_Context.PlanAxis,
                    m_Context.BuccalAxis,
                    out var origin,
                    out var normal,
                    out var up,
                    out var error) ||
                !TryValidateMesialLeft(normal, up, out error))
            {
                ReportWarning(error);
                return;
            }

            m_IsApplying = true;
            try
            {
                if (!m_CtService.SetPatientPlane(origin, normal, up, 0f, out error))
                {
                    ReportWarning(error);
                    return;
                }
                ClearWarning();
            }
            finally
            {
                m_IsApplying = false;
            }
        }

        bool TryConvertPlaneToDicom(
            bool alreadyDicom,
            Vector3 origin,
            Vector3 normal,
            Vector3 up,
            out Vector3 dicomOrigin,
            out Vector3 dicomNormal,
            out Vector3 dicomUp,
            out string error)
        {
            dicomOrigin = origin;
            dicomNormal = normal;
            dicomUp = up;
            error = string.Empty;
            if (alreadyDicom)
                return true;

            if (!m_Context.HasPatientFromDicom || Mathf.Abs(m_Context.PatientFromDicom.determinant) < 1e-8f)
            {
                error = "切片坐标系与 DICOM 不一致，且缺少可逆 patient_from_dicom 变换。";
                return false;
            }

            var dicomFromPatient = m_Context.PatientFromDicom.inverse;
            dicomOrigin = dicomFromPatient.MultiplyPoint3x4(origin);
            dicomNormal = dicomFromPatient.MultiplyVector(normal);
            dicomUp = dicomFromPatient.MultiplyVector(up);
            if (!IsFinite(dicomOrigin) || !IsFinite(dicomNormal) || !IsFinite(dicomUp) ||
                dicomNormal.sqrMagnitude < 1e-8f || dicomUp.sqrMagnitude < 1e-8f)
            {
                error = "patient_from_dicom 变换产生了无效切片平面。";
                return false;
            }
            return true;
        }

        bool TryValidateMesialLeft(Vector3 normal, Vector3 up, out string error)
        {
            error = string.Empty;
            if (!TryConvertDirectionToDicom(m_Context.MesialAxis, out var mesial, out error))
                return false;

            if (normal.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f || mesial.sqrMagnitude < 1e-8f)
            {
                error = "无法校验颊舌和近远中解剖方向。";
                return false;
            }
            var derivedMesialLeft = Vector3.Cross(normal.normalized, up.normalized).normalized;
            if (Vector3.Dot(derivedMesialLeft, mesial.normalized) < 0.8f)
            {
                error = "切片 normal/up 与上下文近中轴不满足“上颊、左近中”的方向约定。";
                return false;
            }
            return true;
        }

        bool TryConvertDirectionToDicom(Vector3 direction, out Vector3 dicomDirection, out string error)
        {
            dicomDirection = direction;
            error = string.Empty;
            var volume = m_CtService.CurrentVolume;
            var contextAlreadyDicom = volume != null && !string.IsNullOrEmpty(volume.FrameOfReferenceUid) &&
                                      string.Equals(m_Context.CoordinateFrameId, volume.FrameOfReferenceUid, StringComparison.Ordinal);
            if (contextAlreadyDicom)
                return true;
            if (!m_Context.HasPatientFromDicom || Mathf.Abs(m_Context.PatientFromDicom.determinant) < 1e-8f)
            {
                error = "缺少解剖方向到 DICOM 坐标的变换。";
                return false;
            }
            dicomDirection = m_Context.PatientFromDicom.inverse.MultiplyVector(direction);
            if (!IsFinite(dicomDirection) || dicomDirection.sqrMagnitude < 1e-8f)
            {
                error = "近中轴变换到 DICOM 坐标后无效。";
                return false;
            }
            return true;
        }

        void ReportWarning(string message)
        {
            var normalized = string.IsNullOrEmpty(message) ? "CT 切片应用失败。" : message;
            m_CtService.ReportSliceStatus(normalized);
            if (string.Equals(normalized, m_LastWarning, StringComparison.Ordinal))
                return;
            m_LastWarning = normalized;
            Debug.LogWarning("DentalCtSliceCoordinator: " + normalized);
        }

        void RejectControl(string message)
        {
            ReportWarning(message);
            RecordControlResult(DentalSliceExecutionDisposition.Rejected, message);
        }

        void RecordControlResult(DentalSliceExecutionDisposition disposition, string error)
        {
            m_LastExecutionSessionId = m_PendingSliceState.SessionId ?? string.Empty;
            m_LastExecutionContextVersion = m_PendingSliceState.ContextVersion;
            m_LastExecutionControlVersion = m_PendingSliceState.ControlVersion;
            m_LastExecutionDisposition = disposition;
            m_LastExecutionError = error ?? string.Empty;
        }

        void ClearWarning()
        {
            m_LastWarning = string.Empty;
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
