using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// One owner for the XREAL Eye RGB camera. Consumers acquire/release capture without
    /// calling XREALRGBCameraTexture.StartCapture or StopCapture themselves.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class RgbCameraFrameService : MonoBehaviour
    {
        const string AndroidCameraPermission = "android.permission.CAMERA";
        const int MaxStartAttempts = 15;
        const float RetryIntervalSeconds = 2f;

        static RgbCameraFrameService s_Instance;

        readonly HashSet<object> m_Consumers = new HashSet<object>();
        XREALRGBCameraTexture m_CameraTexture;
        XREALRGBCameraPlugState m_PlugState = XREALRGBCameraPlugState.UNKNOWN;
        Coroutine m_StartCoroutine;
        RgbCameraFrame m_LatestFrame;
        long m_FrameSequence;
        bool m_Initialized;
        bool m_Initializing;
        bool m_SubscribedToFrames;
        bool m_SubscribedToPlugState;

        public static RgbCameraFrameService Instance => s_Instance;

        public static RgbCameraFrameService EnsureInstance(GameObject host = null)
        {
            if (s_Instance != null)
                return s_Instance;

            s_Instance = FindObjectOfType<RgbCameraFrameService>();
            if (s_Instance != null)
                return s_Instance;

            var serviceHost = host != null ? host : new GameObject("Shared XREAL RGB Camera");
            return serviceHost.AddComponent<RgbCameraFrameService>();
        }

        public event Action<RgbCameraFrame> FrameReceived;

        public bool IsCapturing => m_CameraTexture != null && m_CameraTexture.IsCapturing;
        public bool IsReady => m_Initialized && m_CameraTexture != null;
        public int ConsumerCount => m_Consumers.Count;
        public XREALRGBCameraPlugState PlugState => m_PlugState;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            SubscribeToPlugState();
        }

        IEnumerator Start()
        {
            yield return InitializeAsync();
        }

        void OnEnable()
        {
            SubscribeToPlugState();
            if (m_Initialized)
                SubscribeToFrames();
            if (m_Consumers.Count > 0)
                EnsureCaptureStarted();
        }

        void OnDisable()
        {
            if (m_StartCoroutine != null)
            {
                StopCoroutine(m_StartCoroutine);
                m_StartCoroutine = null;
            }

            StopNativeCapture();
            UnsubscribeFromFrames();
            UnsubscribeFromPlugState();
        }

        void OnDestroy()
        {
            StopNativeCapture();
            UnsubscribeFromFrames();
            UnsubscribeFromPlugState();
            m_Consumers.Clear();
            if (s_Instance == this)
                s_Instance = null;
        }

        public void AcquireCapture(object consumer)
        {
            if (consumer == null)
                throw new ArgumentNullException(nameof(consumer));

            if (!m_Consumers.Add(consumer))
                return;

            if (!m_Initialized && !m_Initializing)
                StartCoroutine(InitializeAsync());
            else
                EnsureCaptureStarted();
        }

        public void ReleaseCapture(object consumer)
        {
            if (consumer == null || !m_Consumers.Remove(consumer))
                return;

            if (m_Consumers.Count == 0)
            {
                if (m_StartCoroutine != null)
                {
                    StopCoroutine(m_StartCoroutine);
                    m_StartCoroutine = null;
                }
                StopNativeCapture();
            }
        }

        public bool TryGetLatestFrame(out RgbCameraFrame frame)
        {
            frame = m_LatestFrame;
            return frame.IsValid;
        }

        IEnumerator InitializeAsync()
        {
            if (m_Initialized || m_Initializing)
                yield break;

            m_Initializing = true;
            yield return RequestPermissionIfNeeded();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!XREALAndroidPermissionsManager.IsPermissionGranted(AndroidCameraPermission))
            {
                Log("Android Camera permission was not granted.", true);
                m_Initializing = false;
                yield break;
            }
#endif

            m_CameraTexture = XREALRGBCameraTexture.CreateSingleton();
            m_Initialized = m_CameraTexture != null;
            m_Initializing = false;
            if (!m_Initialized)
            {
                Log("Could not create XREALRGBCameraTexture.", true);
                yield break;
            }

            SubscribeToFrames();
            Log("Shared RGB camera service is ready.");
            if (m_Consumers.Count > 0)
                EnsureCaptureStarted();
        }

        IEnumerator RequestPermissionIfNeeded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (XREALAndroidPermissionsManager.IsPermissionGranted(AndroidCameraPermission))
                yield break;

            var task = XREALAndroidPermissionsManager.RequestPermission(AndroidCameraPermission);
            if (task == null)
            {
                Log("Another Android permission request is already active.", true);
                yield break;
            }

            yield return task.WaitForCompletion();
#else
            yield break;
#endif
        }

        void EnsureCaptureStarted()
        {
            if (!isActiveAndEnabled || m_Consumers.Count == 0 || m_CameraTexture == null
                || m_CameraTexture.IsCapturing || m_StartCoroutine != null)
                return;

            m_StartCoroutine = StartCoroutine(StartCaptureWithRetry());
        }

        IEnumerator StartCaptureWithRetry()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA))
            {
                Log("The connected XREAL device does not expose an RGB camera.", true);
                m_StartCoroutine = null;
                yield break;
            }
#endif

            for (var attempt = 1; attempt <= MaxStartAttempts && m_Consumers.Count > 0; attempt++)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (m_PlugState == XREALRGBCameraPlugState.PLUGOUT)
                {
                    yield return new WaitForSeconds(RetryIntervalSeconds);
                    continue;
                }
#endif
                if (m_CameraTexture.IsCapturing || m_CameraTexture.StartCapture())
                {
                    Log($"RGB capture started for {m_Consumers.Count} consumer(s).");
                    m_StartCoroutine = null;
                    yield break;
                }

                if (attempt < MaxStartAttempts)
                    yield return new WaitForSeconds(RetryIntervalSeconds);
            }

            if (m_Consumers.Count > 0)
                Log("RGB capture could not be started.", true);
            m_StartCoroutine = null;
        }

        void StopNativeCapture()
        {
            m_LatestFrame = default;
            if (m_CameraTexture != null && m_CameraTexture.IsCapturing)
            {
                m_CameraTexture.StopCapture();
                Log("RGB capture stopped; no active consumer remains.");
            }
        }

        void SubscribeToFrames()
        {
            if (m_CameraTexture == null || m_SubscribedToFrames)
                return;
            m_CameraTexture.OnRGBCameraUpdate += OnCameraUpdated;
            m_SubscribedToFrames = true;
        }

        void UnsubscribeFromFrames()
        {
            if (m_CameraTexture == null || !m_SubscribedToFrames)
                return;
            m_CameraTexture.OnRGBCameraUpdate -= OnCameraUpdated;
            m_SubscribedToFrames = false;
        }

        void OnCameraUpdated()
        {
            if (m_CameraTexture == null)
                return;

            var textures = m_CameraTexture.GetYUVFormatTextures();
            if (textures == null || textures.Length < 3
                || textures[0] == null || textures[1] == null || textures[2] == null)
                return;

            m_LatestFrame = new RgbCameraFrame(
                textures[0], textures[1], textures[2],
                m_CameraTexture.GetTimeStamp(), ++m_FrameSequence,
                Time.realtimeSinceStartupAsDouble);
            FrameReceived?.Invoke(m_LatestFrame);
        }

        void SubscribeToPlugState()
        {
            if (m_SubscribedToPlugState)
                return;
            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState += OnPlugStateChanged;
            m_SubscribedToPlugState = true;
        }

        void UnsubscribeFromPlugState()
        {
            if (!m_SubscribedToPlugState)
                return;
            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState -= OnPlugStateChanged;
            m_SubscribedToPlugState = false;
        }

        void OnPlugStateChanged(XREALRGBCameraPlugState state)
        {
            m_PlugState = state;
            if (state == XREALRGBCameraPlugState.PLUGOUT)
            {
                if (m_StartCoroutine != null)
                {
                    StopCoroutine(m_StartCoroutine);
                    m_StartCoroutine = null;
                }
                StopNativeCapture();
            }
            else if (state == XREALRGBCameraPlugState.PLUGIN && m_Consumers.Count > 0)
            {
                EnsureCaptureStarted();
            }
        }

        static void Log(string message, bool warning = false)
        {
            BeamProUnifiedLogWindow.SetStatus("RGB 相机", message);
            BeamProUnifiedLogWindow.AddLine("RGB 相机", message);
            if (warning)
                Debug.LogWarning($"RgbCameraFrameService: {message}");
            else
                Debug.Log($"RgbCameraFrameService: {message}");
        }
    }

    public readonly struct RgbCameraFrame
    {
        public RgbCameraFrame(Texture y, Texture u, Texture v, ulong timestamp, long sequence, double receivedAtSeconds)
        {
            Y = y;
            U = u;
            V = v;
            Timestamp = timestamp;
            Sequence = sequence;
            ReceivedAtSeconds = receivedAtSeconds;
        }

        public Texture Y { get; }
        public Texture U { get; }
        public Texture V { get; }
        public ulong Timestamp { get; }
        public long Sequence { get; }
        public double ReceivedAtSeconds { get; }
        public bool IsValid => Y != null && U != null && V != null;
    }
}
