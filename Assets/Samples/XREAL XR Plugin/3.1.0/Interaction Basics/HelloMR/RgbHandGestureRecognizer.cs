using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Converts frames from the shared RGB owner into landmark observations through a
    /// pluggable offline provider, then classifies the OK pose on the main thread.
    /// </summary>
    public class RgbHandGestureRecognizer : MonoBehaviour
    {
        public const string LogSource = "手势识别";
        const float GestureStaleSeconds = 0.18f;
        const float StatusIntervalSeconds = 0.5f;

        [SerializeField] bool m_RecognitionEnabled;
        [SerializeField] float m_ProcessIntervalSeconds = 1f / 15f;
        [SerializeField] bool m_LogDebugFeatures = true;

        static RgbHandGestureRecognizer s_Instance;

        readonly object m_PendingLock = new object();
        readonly RgbHandGestureAnalyzer m_Analyzer = new RgbHandGestureAnalyzer();
        RgbCameraFrameService m_CameraService;
        IRgbHandLandmarkProvider m_Provider;
        RgbHandLandmarkFrame m_PendingLandmarks;
        bool m_HasPendingLandmarks;
        bool m_HasCameraLease;
        double m_NextSubmitTime;
        double m_LastObservationTime = double.NegativeInfinity;
        double m_NextStatusTime;

        public static RgbHandGestureRecognizer Instance => s_Instance;
        public RgbHandGesture CurrentGesture { get; private set; } = RgbHandGesture.None;
        public RgbHandGestureObservation LastObservation { get; private set; }
        public bool RecognitionEnabled => m_RecognitionEnabled;
        public bool ProviderAvailable => m_Provider != null && m_Provider.IsAvailable;
        public string ProviderName => m_Provider != null ? m_Provider.Name : "not created";
        public string ProviderStatus => m_Provider != null ? m_Provider.Status : "not created";

        public event Action<RgbHandGesture> GestureChanged;
        public event EventHandler<RgbHandGestureChangedEventArgs> GestureChangedDetailed;
        public event Action<RgbHandGestureObservation> ObservationUpdated;
        public static event Action<RgbHandGesture> AnyGestureChanged;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
            s_Instance = this;
            m_CameraService = RgbCameraFrameService.EnsureInstance(gameObject);
        }

        void OnEnable()
        {
            if (s_Instance == null)
                s_Instance = this;
            if (m_RecognitionEnabled)
                StartProviderAndCamera();
        }

        void OnDisable()
        {
            StopProviderAndCamera();
            SetCurrentGesture(RgbHandGesture.None);
        }

        void OnDestroy()
        {
            StopProviderAndCamera();
            DisposeProvider();
            if (s_Instance == this)
                s_Instance = null;
        }

        void Update()
        {
            ConsumeLandmarks();

            var now = Time.realtimeSinceStartupAsDouble;
            if (CurrentGesture != RgbHandGesture.None && now - m_LastObservationTime > GestureStaleSeconds)
                SetCurrentGesture(RgbHandGesture.None);

            if (now >= m_NextStatusTime)
            {
                m_NextStatusTime = now + StatusIntervalSeconds;
                PublishStatus();
            }
        }

        public void SetRecognitionEnabled(bool enabled)
        {
            if (m_RecognitionEnabled == enabled)
                return;

            m_RecognitionEnabled = enabled;
            if (enabled)
            {
                StartProviderAndCamera();
                LogStatus(ProviderAvailable
                    ? $"手势识别已开启，provider={ProviderName}。"
                    : $"手势识别不可用：{ProviderStatus}", !ProviderAvailable);
            }
            else
            {
                StopProviderAndCamera();
                SetCurrentGesture(RgbHandGesture.None);
                LogStatus("手势识别已关闭。");
            }
            PublishStatus();
        }

        public void ToggleRecognitionEnabled()
        {
            SetRecognitionEnabled(!m_RecognitionEnabled);
        }

        /// <summary>Re-evaluates the registered runtime provider, for late native bootstrap.</summary>
        public void RefreshProvider()
        {
            var wasEnabled = m_RecognitionEnabled;
            StopProviderAndCamera();
            DisposeProvider();
            if (wasEnabled)
                StartProviderAndCamera();
        }

        /// <summary>Injects deterministic landmarks without enabling a pixel heuristic.</summary>
        public void SetProviderForTesting(IRgbHandLandmarkProvider provider)
        {
            StopProviderAndCamera();
            DisposeProvider();
            m_Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (m_RecognitionEnabled)
                StartProviderAndCamera();
        }

        void StartProviderAndCamera()
        {
            if (!m_RecognitionEnabled || !isActiveAndEnabled)
                return;

            if (m_Provider == null)
                m_Provider = RgbHandLandmarkProviderRegistry.CreateProvider();
            if (!m_Provider.IsAvailable)
                return;

            m_Provider.LandmarksReady -= OnLandmarksReady;
            m_Provider.LandmarksReady += OnLandmarksReady;
            if (!m_Provider.IsRunning && !m_Provider.Start())
            {
                LogStatus($"无法启动 landmark provider：{m_Provider.Status}", true);
                return;
            }

            if (m_CameraService == null)
                m_CameraService = RgbCameraFrameService.EnsureInstance(gameObject);
            m_CameraService.FrameReceived -= OnRgbFrame;
            m_CameraService.FrameReceived += OnRgbFrame;
            if (!m_HasCameraLease)
            {
                m_HasCameraLease = true;
                m_CameraService.AcquireCapture(this);
            }
        }

        void StopProviderAndCamera()
        {
            if (m_CameraService != null)
            {
                m_CameraService.FrameReceived -= OnRgbFrame;
                if (m_HasCameraLease)
                    m_CameraService.ReleaseCapture(this);
            }
            m_HasCameraLease = false;

            if (m_Provider != null)
            {
                m_Provider.LandmarksReady -= OnLandmarksReady;
                m_Provider.Stop();
            }

            lock (m_PendingLock)
                m_HasPendingLandmarks = false;
        }

        void DisposeProvider()
        {
            if (m_Provider == null)
                return;
            m_Provider.LandmarksReady -= OnLandmarksReady;
            m_Provider.Dispose();
            m_Provider = null;
        }

        void OnRgbFrame(RgbCameraFrame frame)
        {
            if (!m_RecognitionEnabled || m_Provider == null || !m_Provider.IsRunning)
                return;
            if (frame.ReceivedAtSeconds < m_NextSubmitTime)
                return;
            m_NextSubmitTime = frame.ReceivedAtSeconds + Mathf.Max(0.02f, m_ProcessIntervalSeconds);
            m_Provider.TrySubmitFrame(frame);
        }

        void OnLandmarksReady(RgbHandLandmarkFrame frame)
        {
            lock (m_PendingLock)
            {
                m_PendingLandmarks = frame;
                m_HasPendingLandmarks = true;
            }
        }

        void ConsumeLandmarks()
        {
            RgbHandLandmarkFrame frame;
            lock (m_PendingLock)
            {
                if (!m_HasPendingLandmarks)
                    return;
                frame = m_PendingLandmarks;
                m_HasPendingLandmarks = false;
            }

            var observation = m_Analyzer.Analyze(frame);
            LastObservation = observation;
            m_LastObservationTime = Time.realtimeSinceStartupAsDouble;
            SetCurrentGesture(observation.Gesture);
            ObservationUpdated?.Invoke(observation);

            if (m_LogDebugFeatures)
            {
                BeamProUnifiedLogWindow.SetStatus(
                    LogSource,
                    $"provider={ProviderName} gesture={RgbHandGestureNames.ToChinese(observation.Gesture)} " +
                    $"palm=({observation.PalmPosition.x:0.00},{observation.PalmPosition.y:0.00}) conf={observation.Confidence:0.00}");
            }
        }

        void SetCurrentGesture(RgbHandGesture gesture)
        {
            if (gesture == CurrentGesture)
                return;
            var previous = CurrentGesture;
            CurrentGesture = gesture;
            GestureChanged?.Invoke(gesture);
            GestureChangedDetailed?.Invoke(this, new RgbHandGestureChangedEventArgs(previous, gesture));
            AnyGestureChanged?.Invoke(gesture);
        }

        void PublishStatus()
        {
            var enabled = m_RecognitionEnabled ? "开" : "关";
            var availability = ProviderAvailable ? ProviderStatus : $"不可用: {ProviderStatus}";
            BeamProUnifiedLogWindow.SetStatus(
                LogSource,
                $"识别={enabled} | provider={ProviderName} | {availability}\n手势={RgbHandGestureNames.ToChinese(CurrentGesture)}");
        }

        static void LogStatus(string message, bool warning = false)
        {
            BeamProUnifiedLogWindow.AddLine(LogSource, message);
            if (warning)
                Debug.LogWarning($"RgbHandGestureRecognizer: {message}");
            else
                Debug.Log($"RgbHandGestureRecognizer: {message}");
        }
    }
}
