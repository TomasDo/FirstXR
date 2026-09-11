using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Offline Android MediaPipe hand landmark provider. RGB conversion is GPU-backed and the
    /// Java bridge owns a single worker with a one-frame queue, preventing inference backlog.
    /// </summary>
    public sealed class MediaPipeAndroidHandLandmarkProvider : IRgbHandLandmarkProvider
    {
        const int InputWidth = 256;
        const int InputHeight = 144;
        const int ResultHeaderLength = 4;
        const string BridgeClass = "com.firstxr.mediapipe.HandLandmarkerBridge";
        const string ModelAssetPath = "mediapipe/hand_landmarker.task";

        AndroidJavaObject m_Bridge;
        Material m_YuvMaterial;
        RenderTexture m_InputTexture;
        bool m_Running;
        bool m_ReadbackPending;
        bool m_Disposed;
        long m_LastTimestampMs;
        string m_Status;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterFactory()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            RgbHandLandmarkProviderRegistry.RegisterFactory(() => new MediaPipeAndroidHandLandmarkProvider());
#endif
        }

        public MediaPipeAndroidHandLandmarkProvider()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    m_Bridge = new AndroidJavaObject(BridgeClass, activity, ModelAssetPath);
                }
                m_Status = NativeStatus();
            }
            catch (Exception error)
            {
                m_Status = "MediaPipe bridge creation failed: " + error.Message;
                DisposeBridge();
            }
#else
            m_Status = "MediaPipe hand landmarks are available only in an Android player.";
#endif
        }

        public string Name => "MediaPipe Hand Landmarker 1.0.0";
        public string Status => NativeStatus();
        public bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return !m_Disposed && m_Bridge != null && m_Bridge.Call<bool>("isReady"); }
                catch { return false; }
#else
                return false;
#endif
            }
        }
        public bool IsRunning => m_Running && IsAvailable;

        public event Action<RgbHandLandmarkFrame> LandmarksReady;

        public bool Start()
        {
            if (m_Disposed || !IsAvailable)
                return false;

            if (m_YuvMaterial == null)
            {
                var shader = Shader.Find("Unlit/YUVTransRGB");
                if (shader == null)
                {
                    m_Status = "YUVTransRGB shader is unavailable.";
                    return false;
                }
                m_YuvMaterial = new Material(shader) { name = "MediaPipe YUV to RGB" };
            }

            if (m_InputTexture == null)
            {
                m_InputTexture = new RenderTexture(InputWidth, InputHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "MediaPipe Hand Input",
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                m_InputTexture.Create();
            }

            m_Running = true;
            return true;
        }

        public void Stop()
        {
            m_Running = false;
        }

        public bool TrySubmitFrame(RgbCameraFrame frame)
        {
            PollResult();
            if (!IsRunning || !frame.IsValid || m_ReadbackPending)
                return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (m_Bridge.Call<bool>("isBusy"))
                    return false;

                m_YuvMaterial.SetTexture("_MainTex", frame.Y);
                m_YuvMaterial.SetTexture("_UTex", frame.U);
                m_YuvMaterial.SetTexture("_VTex", frame.V);
                Graphics.Blit(frame.Y, m_InputTexture, m_YuvMaterial);

                m_ReadbackPending = true;
                var sourceSequence = frame.Sequence;
                var observedAtSeconds = frame.ReceivedAtSeconds;
                var timestampMs = Math.Max(m_LastTimestampMs + 1L, (long)(observedAtSeconds * 1000.0));
                m_LastTimestampMs = timestampMs;
                AsyncGPUReadback.Request(m_InputTexture, 0, TextureFormat.RGBA32, request =>
                {
                    m_ReadbackPending = false;
                    if (!m_Running || m_Disposed || request.hasError || m_Bridge == null)
                        return;

                    try
                    {
                        NativeArray<byte> pixels = request.GetData<byte>();
                        m_Bridge.Call<bool>(
                            "submitRgba",
                            pixels.ToArray(), InputWidth, InputHeight,
                            timestampMs, sourceSequence, observedAtSeconds);
                    }
                    catch (Exception error)
                    {
                        m_Status = "MediaPipe frame submission failed: " + error.Message;
                    }
                });
                return true;
            }
            catch (Exception error)
            {
                m_ReadbackPending = false;
                m_Status = "MediaPipe RGB conversion failed: " + error.Message;
                return false;
            }
#else
            return false;
#endif
        }

        void PollResult()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Bridge == null || m_Disposed)
                return;

            try
            {
                var packet = m_Bridge.Call<double[]>("consumeLatestResult");
                if (packet == null || packet.Length < ResultHeaderLength)
                    return;

                var sequence = (long)packet[0];
                var observedAt = packet[1];
                var confidence = Mathf.Clamp01((float)packet[2]);
                var tracked = packet[3] > 0.5;
                if (!tracked || packet.Length < ResultHeaderLength + RgbHandLandmarkFrame.LandmarkCount * 3)
                {
                    LandmarksReady?.Invoke(RgbHandLandmarkFrame.NotTracked(sequence, observedAt));
                    return;
                }

                var landmarks = new Vector3[RgbHandLandmarkFrame.LandmarkCount];
                for (var index = 0; index < landmarks.Length; index++)
                {
                    var input = ResultHeaderLength + index * 3;
                    landmarks[index] = new Vector3(
                        (float)packet[input],
                        (float)packet[input + 1],
                        (float)packet[input + 2]);
                }
                LandmarksReady?.Invoke(new RgbHandLandmarkFrame(
                    landmarks, confidence, true, sequence, observedAt));
            }
            catch (Exception error)
            {
                m_Status = "MediaPipe result polling failed: " + error.Message;
            }
#endif
        }

        string NativeStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Disposed)
                return "closed";
            if (m_Bridge == null)
                return string.IsNullOrEmpty(m_Status) ? "bridge unavailable" : m_Status;
            try { return m_Bridge.Call<string>("getStatus"); }
            catch { return string.IsNullOrEmpty(m_Status) ? "bridge unavailable" : m_Status; }
#else
            return m_Status;
#endif
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            m_Running = false;
            DisposeBridge();

            if (m_InputTexture != null)
            {
                m_InputTexture.Release();
                UnityEngine.Object.Destroy(m_InputTexture);
                m_InputTexture = null;
            }
            if (m_YuvMaterial != null)
            {
                UnityEngine.Object.Destroy(m_YuvMaterial);
                m_YuvMaterial = null;
            }
        }

        void DisposeBridge()
        {
            if (m_Bridge == null)
                return;
            try { m_Bridge.Call("close"); }
            catch { }
            m_Bridge.Dispose();
            m_Bridge = null;
        }
    }
}
