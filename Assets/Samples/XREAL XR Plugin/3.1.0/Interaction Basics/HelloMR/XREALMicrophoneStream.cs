using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.XR.XREAL;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// PCM chunk from the XREAL glasses microphone via <see cref="XREALAudioCapture"/>.
    /// Format: 16-bit signed integer, interleaved channels (see <see cref="Channels"/>).
    /// </summary>
    public readonly struct XREALAudioPcmChunk
    {
        public XREALAudioPcmChunk(byte[] data, int sampleCount, int channels, int sampleRate, int bytesPerSample)
        {
            Data = data;
            SampleCount = sampleCount;
            Channels = channels;
            SampleRate = sampleRate;
            BytesPerSample = bytesPerSample;
        }

        public byte[] Data { get; }
        public int SampleCount { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public int BytesPerSample { get; }
    }

    /// <summary>
    /// Captures the XREAL glasses microphone as a PCM byte stream using the native XREAL audio pipeline.
    /// Subscribe to <see cref="OnPcmChunk"/> or poll <see cref="TryReadBufferedPcm"/> after <see cref="StartCapture"/>.
    /// </summary>
    public class XREALMicrophoneStream : MonoBehaviour
    {
        const string AndroidRecordAudioPermission = "android.permission.RECORD_AUDIO";
        const string TempRecordingFileName = "xreal_mic_stream.m4a";
        const int MaxQueuedPcmBytes = 1024 * 1024 * 4;

        [SerializeField]
        bool m_StartCaptureOnAwake = true;

        [SerializeField]
        bool m_UseMonophonic = true;

        [SerializeField]
        [Range(0.1f, 5f)]
        float m_MicVolumeFactor = 1f;

        [SerializeField]
        bool m_DeleteTempRecordingOnStop = true;

        [SerializeField]
        bool m_ShowDebugOverlayOnBeamPro = true;

        [SerializeField]
        float m_DebugLogIntervalSeconds = 2f;

        XREALAudioCapture m_AudioCapture;
        string m_TempRecordingPath;
        bool m_IsCapturing;
        bool m_PendingStart;
        ulong m_TotalBytesReceived;
        int m_LastChunkBytes;
        float m_NextDebugLogTime;
        string m_StatusText = "Mic stream: idle";
        GUIStyle m_OverlayStyle;
        readonly Queue<XREALAudioPcmChunk> m_PcmQueue = new Queue<XREALAudioPcmChunk>(32);
        readonly object m_PcmQueueLock = new object();
        int m_QueuedPcmBytes;
        static readonly Color s_OverlayTextColor = new Color(0.35f, 0.85f, 1f);

        /// <summary> Fired on the Unity main thread when a native audio buffer arrives. </summary>
        public event Action<XREALAudioPcmChunk> OnPcmChunk;

        public bool IsCapturing => m_IsCapturing;

        public int SampleRate => m_UseMonophonic ? NativeConstants.RECORD_AUDIO_SAMPLERATE_MONO : NativeConstants.RECORD_AUDIO_SAMPLERATE_DEFAULT;

        public int Channels => m_UseMonophonic ? NativeConstants.RECORD_AUDIO_CHANNEL_MONO : NativeConstants.RECORD_AUDIO_CHANNEL;

        public int BytesPerSample => m_AudioCapture != null ? m_AudioCapture.BytesPerSample : NativeConstants.RECORD_AUDIO_BYTES_PER_SAMPLE;

        void Start()
        {
            if (m_StartCaptureOnAwake)
                StartCapture();
        }

        void OnDestroy()
        {
            StopCapture();
        }

        void Update()
        {
            while (TryDequeuePcmChunk(out var chunk))
                DispatchPcmChunkOnMainThread(chunk);
        }

        /// <summary> Begin microphone capture (requests RECORD_AUDIO on Android). </summary>
        public void StartCapture()
        {
            if (m_IsCapturing || m_PendingStart)
                return;

            m_PendingStart = true;
            StartCoroutine(StartCaptureWhenReady());
        }

        /// <summary> Stop capture and release native resources. </summary>
        public void StopCapture()
        {
            if (!m_IsCapturing && m_AudioCapture == null)
            {
                m_PendingStart = false;
                return;
            }

            m_PendingStart = false;

            if (m_AudioCapture != null)
            {
                m_AudioCapture.OnAudioData -= HandleNativeAudioData;

                if (m_AudioCapture.IsRecording)
                    m_AudioCapture.StopRecordingAsync(_ => { });

                m_AudioCapture.StopAudioModeAsync(_ => { });
                m_AudioCapture.Dispose();
                m_AudioCapture = null;
            }

            if (m_DeleteTempRecordingOnStop && !string.IsNullOrEmpty(m_TempRecordingPath) && File.Exists(m_TempRecordingPath))
            {
                try
                {
                    File.Delete(m_TempRecordingPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[XREALMic] Failed to delete temp recording: {ex.Message}");
                }
            }

            m_IsCapturing = false;
            m_StatusText = "Mic stream: stopped";
            ClearQueuedPcm();
        }

        /// <summary>
        /// Pull any PCM bytes buffered inside <see cref="XREALAudioCapture"/> since the last call.
        /// </summary>
        public bool TryReadBufferedPcm(ref byte[] buffer, out int sampleCount)
        {
            sampleCount = 0;
            if (m_AudioCapture == null)
                return false;

            if (!m_AudioCapture.FlushAudioData(ref buffer, ref sampleCount) || sampleCount <= 0)
                return false;

            DispatchPcmChunkOnMainThread(new XREALAudioPcmChunk(buffer, sampleCount, Channels, SampleRate, BytesPerSample));
            return true;
        }

        IEnumerator StartCaptureWhenReady()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return RequestRecordAudioPermissionIfNeeded();
            if (!HasRecordAudioPermission())
            {
                m_StatusText = "Mic stream: RECORD_AUDIO denied";
                m_PendingStart = false;
                yield break;
            }
#else
            if (Application.isEditor)
            {
                m_StatusText = "Mic stream: editor (no XREAL mic)";
                Debug.LogWarning("[XREALMic] Microphone capture runs on Android device with XREAL glasses.");
                m_PendingStart = false;
                yield break;
            }
#endif

            m_AudioCapture = XREALAudioCapture.Create();
            if (m_AudioCapture == null)
            {
                m_StatusText = "Mic stream: failed to create capture";
                m_PendingStart = false;
                yield break;
            }

            m_AudioCapture.OnAudioData += HandleNativeAudioData;
            m_TempRecordingPath = Path.Combine(Application.temporaryCachePath, TempRecordingFileName);

            var setupParams = new CameraParameters(CamMode.None, BlendMode.Blend)
            {
                audioState = AudioState.MicAudio,
                monophonic = m_UseMonophonic,
                frameRate = NativeConstants.RECORD_FPS_DEFAULT,
            };

            m_StatusText = "Mic stream: starting audio mode...";
            m_AudioCapture.StartAudioModeAsync(setupParams, OnAudioModeStarted);
            m_PendingStart = false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        IEnumerator RequestRecordAudioPermissionIfNeeded()
        {
            if (HasRecordAudioPermission())
                yield break;

            m_StatusText = "Mic stream: requesting RECORD_AUDIO...";
            var permissionTask = XREALAndroidPermissionsManager.RequestPermission(AndroidRecordAudioPermission);
            if (permissionTask == null)
            {
                m_StatusText = "Mic stream: permission request busy";
                yield break;
            }

            yield return permissionTask.WaitForCompletion();

            if (permissionTask.Result.IsAllGranted)
                m_StatusText = "Mic stream: RECORD_AUDIO granted";
            else
                m_StatusText = $"Mic stream: RECORD_AUDIO denied ({permissionTask.Result})";
        }

        static bool HasRecordAudioPermission()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return activity.Call<int>("checkSelfPermission", AndroidRecordAudioPermission) == 0;
        }
#endif

        void OnAudioModeStarted(XREALAudioCapture.AudioCaptureResult result)
        {
            if (m_AudioCapture == null)
                return;

            if (!result.success)
            {
                m_StatusText = "Mic stream: audio mode failed";
                Debug.LogError("[XREALMic] StartAudioModeAsync failed.");
                StopCapture();
                return;
            }

            m_StatusText = "Mic stream: starting recorder...";
            m_AudioCapture.StartRecordingAsync(m_TempRecordingPath, OnRecordingStarted, m_MicVolumeFactor, 1f);
        }

        void OnRecordingStarted(XREALAudioCapture.AudioCaptureResult result)
        {
            if (!result.success)
            {
                m_StatusText = "Mic stream: recorder failed";
                Debug.LogError("[XREALMic] StartRecordingAsync failed.");
                StopCapture();
                return;
            }

            m_IsCapturing = true;
            m_TotalBytesReceived = 0;
            m_StatusText = $"Mic stream: capturing ({SampleRate} Hz, {Channels} ch)";
            Debug.Log($"[XREALMic] Capturing glasses mic at {SampleRate} Hz, {Channels} channel(s), {BytesPerSample * 8}-bit PCM.");
        }

        void HandleNativeAudioData(IntPtr data, uint size)
        {
            if (data == IntPtr.Zero || size == 0)
                return;

            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, (int)size);
            var sampleCount = (int)size / (Channels * BytesPerSample);
            if (sampleCount <= 0)
                return;

            EnqueuePcmChunk(new XREALAudioPcmChunk(bytes, sampleCount, Channels, SampleRate, BytesPerSample));
        }

        void EnqueuePcmChunk(XREALAudioPcmChunk chunk)
        {
            lock (m_PcmQueueLock)
            {
                while (m_QueuedPcmBytes + chunk.Data.Length > MaxQueuedPcmBytes && m_PcmQueue.Count > 0)
                {
                    var dropped = m_PcmQueue.Dequeue();
                    m_QueuedPcmBytes -= dropped.Data.Length;
                }

                m_PcmQueue.Enqueue(chunk);
                m_QueuedPcmBytes += chunk.Data.Length;
            }
        }

        bool TryDequeuePcmChunk(out XREALAudioPcmChunk chunk)
        {
            lock (m_PcmQueueLock)
            {
                if (m_PcmQueue.Count == 0)
                {
                    chunk = default(XREALAudioPcmChunk);
                    return false;
                }

                chunk = m_PcmQueue.Dequeue();
                m_QueuedPcmBytes -= chunk.Data.Length;
                return true;
            }
        }

        void ClearQueuedPcm()
        {
            lock (m_PcmQueueLock)
            {
                m_PcmQueue.Clear();
                m_QueuedPcmBytes = 0;
            }
        }

        void DispatchPcmChunkOnMainThread(XREALAudioPcmChunk chunk)
        {
            m_TotalBytesReceived += (ulong)chunk.Data.Length;
            m_LastChunkBytes = chunk.Data.Length;
            OnPcmChunk?.Invoke(chunk);

            if (Time.unscaledTime >= m_NextDebugLogTime)
            {
                m_NextDebugLogTime = Time.unscaledTime + m_DebugLogIntervalSeconds;
                Debug.Log($"[XREALMic] PCM chunk samples={chunk.SampleCount}, bytes={chunk.Data.Length}, total={m_TotalBytesReceived}");
            }
        }

        void OnGUI()
        {
            if (!m_ShowDebugOverlayOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            var rgbHeight = BeamProOverlayLayout.EstimateRgbPanelHeight(520f);
            var rect = BeamProOverlayLayout.GetMicrophoneOverlayRect(
                BeamProOverlayLayout.MaxButtonRows,
                rgbHeight);

            EnsureOverlayStyle();
            GUI.depth = 11;
            GUI.Box(rect, GUIContent.none);

            var text = $"{m_StatusText}\n" +
                       $"Last chunk: {m_LastChunkBytes} B | Total: {m_TotalBytesReceived} B\n" +
                       $"Format: {SampleRate} Hz, {Channels} ch, {BytesPerSample * 8}-bit PCM";
            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f), text, m_OverlayStyle);
        }

        void EnsureOverlayStyle()
        {
            var fontSize = Mathf.Max(13, Screen.height / 72);
            if (m_OverlayStyle != null && m_OverlayStyle.fontSize == fontSize)
                return;

            m_OverlayStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                wordWrap = true,
                richText = false,
            };
            m_OverlayStyle.normal.textColor = s_OverlayTextColor;
        }
    }
}
