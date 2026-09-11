using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Recognizes open palm / fist / pinch from the existing XREAL Eye RGB camera stream.
    /// Subscribe to <see cref="GestureChanged"/> or read <see cref="CurrentGesture"/>.
    /// </summary>
    public class RgbHandGestureRecognizer : MonoBehaviour
    {
        public const string LogSource = "手势识别";

        [SerializeField]
        bool m_RecognitionEnabled = false;

        [SerializeField]
        int m_AnalysisWidth = 160;

        [SerializeField]
        int m_AnalysisHeight = 90;

        [SerializeField]
        float m_ProcessIntervalSeconds = 0.12f;

        [SerializeField]
        int m_ConfirmFrames = 3;

        [SerializeField]
        int m_NoneConfirmFrames = 4;

        [SerializeField]
        bool m_LogDebugFeatures = true;

        const float DebugFeatureLogIntervalSeconds = 3f;

        static RgbHandGestureRecognizer s_Instance;

        readonly RgbHandGestureAnalyzer m_Analyzer = new RgbHandGestureAnalyzer();
        readonly object m_ObservationLock = new object();

        RGBCameraFloatingWindow m_RgbWindow;
        Material m_YuvMaterial;
        RenderTexture m_AnalysisRT;
        Color32[] m_Pixels;
        RgbHandGestureObservation m_LatestObservation;
        RgbHandGesture m_Candidate = RgbHandGesture.None;
        int m_CandidateCount;
        float m_NextProcessTime;
        float m_NextDebugLogTime;
        bool m_ReadbackPending;
        volatile bool m_AnalysisBusy;
        volatile bool m_HasObservation;
        bool m_LoggedWaitingForRgb;
        bool m_LoggedReady;

        public static RgbHandGestureRecognizer Instance => s_Instance;

        public RgbHandGesture CurrentGesture { get; private set; } = RgbHandGesture.None;

        public RgbHandGestureObservation LastObservation { get; private set; }

        public bool RecognitionEnabled => m_RecognitionEnabled;

        /// <summary>
        /// Fired on the main thread when the debounced gesture changes.
        /// </summary>
        public event Action<RgbHandGesture> GestureChanged;

        public event EventHandler<RgbHandGestureChangedEventArgs> GestureChangedDetailed;

        public static event Action<RgbHandGesture> AnyGestureChanged;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
        }

        void OnEnable()
        {
            if (s_Instance == null)
                s_Instance = this;
        }

        void OnDisable()
        {
            if (CurrentGesture != RgbHandGesture.None)
                SetCurrentGesture(RgbHandGesture.None);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            if (m_YuvMaterial != null)
                Destroy(m_YuvMaterial);
            if (m_AnalysisRT != null)
            {
                m_AnalysisRT.Release();
                Destroy(m_AnalysisRT);
            }
        }

        public void SetRecognitionEnabled(bool enabled)
        {
            if (m_RecognitionEnabled == enabled)
                return;

            m_RecognitionEnabled = enabled;
            if (!enabled)
            {
                m_Candidate = RgbHandGesture.None;
                m_CandidateCount = 0;
                SetCurrentGesture(RgbHandGesture.None);
                LogStatus("手势识别已关闭。");
            }
            else
            {
                m_LoggedWaitingForRgb = false;
                LogStatus("手势识别已开启，等待 Eye RGB 画面。");
            }

            PublishStatus();
        }

        public void ToggleRecognitionEnabled()
        {
            SetRecognitionEnabled(!m_RecognitionEnabled);
        }

        void Update()
        {
            ConsumePendingObservation();

            if (!m_RecognitionEnabled)
            {
                PublishStatus();
                return;
            }

            if (m_RgbWindow == null)
                m_RgbWindow = FindObjectOfType<RGBCameraFloatingWindow>();

            if (!TryGetYuvTextures(out var y, out var u, out var v))
            {
                if (!m_LoggedWaitingForRgb)
                {
                    LogStatus("等待 RGB 相机画面（需 XREAL Eye 已插入并开始采集）。");
                    m_LoggedWaitingForRgb = true;
                }

                PublishStatus();
                return;
            }

            if (!m_LoggedReady)
            {
                LogStatus("已接入 Eye RGB 画面，开始识别张开 / 握拳 / 捏合。");
                m_LoggedReady = true;
            }

            if (m_ReadbackPending || m_AnalysisBusy)
            {
                PublishStatus();
                return;
            }

            if (Time.realtimeSinceStartup < m_NextProcessTime)
            {
                PublishStatus();
                return;
            }

            m_NextProcessTime = Time.realtimeSinceStartup + m_ProcessIntervalSeconds;
            RequestAnalysisFrame(y, u, v);
            PublishStatus();
        }

        bool TryGetYuvTextures(out Texture y, out Texture u, out Texture v)
        {
            y = u = v = null;
            if (m_RgbWindow != null && m_RgbWindow.TryGetYuvTextures(out y, out u, out v))
                return true;

            var cameraTexture = XREALRGBCameraTexture.CreateSingleton();
            if (cameraTexture == null)
                return false;

            var yuv = cameraTexture.GetYUVFormatTextures();
            if (yuv == null || yuv.Length < 3 || yuv[0] == null || yuv[1] == null || yuv[2] == null)
                return false;

            y = yuv[0];
            u = yuv[1];
            v = yuv[2];
            return true;
        }

        void RequestAnalysisFrame(Texture y, Texture u, Texture v)
        {
            if (!EnsureAnalysisTargets())
                return;

            m_YuvMaterial.SetTexture("_MainTex", y);
            m_YuvMaterial.SetTexture("_UTex", u);
            m_YuvMaterial.SetTexture("_VTex", v);
            Graphics.Blit(y, m_AnalysisRT, m_YuvMaterial);

            m_ReadbackPending = true;
            AsyncGPUReadback.Request(m_AnalysisRT, 0, TextureFormat.RGBA32, OnReadback);
        }

        bool EnsureAnalysisTargets()
        {
            var width = Mathf.Max(32, m_AnalysisWidth);
            var height = Mathf.Max(18, m_AnalysisHeight);

            if (m_YuvMaterial == null)
            {
                if (m_RgbWindow != null)
                    m_YuvMaterial = m_RgbWindow.CreateYuvMaterialInstance();

                if (m_YuvMaterial == null)
                {
                    var shader = Shader.Find("Unlit/YUVTransRGB");
                    if (shader == null)
                    {
                        LogStatus("找不到 Unlit/YUVTransRGB shader，无法分析 RGB 帧。", true);
                        return false;
                    }

                    m_YuvMaterial = new Material(shader);
                }
            }

            if (m_AnalysisRT == null || m_AnalysisRT.width != width || m_AnalysisRT.height != height)
            {
                if (m_AnalysisRT != null)
                {
                    m_AnalysisRT.Release();
                    Destroy(m_AnalysisRT);
                }

                m_AnalysisRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "RgbHandGestureAnalysis",
                };
                m_AnalysisRT.Create();
                m_Pixels = new Color32[width * height];
            }

            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            m_ReadbackPending = false;
            if (this == null)
                return;

            if (!m_RecognitionEnabled || m_AnalysisRT == null || request.hasError)
                return;

            var data = request.GetData<Color32>();
            var width = m_AnalysisRT.width;
            var height = m_AnalysisRT.height;
            var count = width * height;
            if (data.Length < count)
            {
                data.Dispose();
                return;
            }

            if (m_Pixels == null || m_Pixels.Length < count)
                m_Pixels = new Color32[count];

            for (var i = 0; i < count; i++)
                m_Pixels[i] = data[i];
            data.Dispose();
            m_AnalysisBusy = true;
            var pixels = m_Pixels;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var observation = m_Analyzer.Analyze(pixels, width, height);
                    lock (m_ObservationLock)
                    {
                        m_LatestObservation = observation;
                        m_HasObservation = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"RgbHandGestureRecognizer: analysis failed: {ex.Message}");
                }
                finally
                {
                    m_AnalysisBusy = false;
                }
            });
        }

        void ConsumePendingObservation()
        {
            if (!m_HasObservation)
                return;

            RgbHandGestureObservation observation;
            lock (m_ObservationLock)
            {
                if (!m_HasObservation)
                    return;
                observation = m_LatestObservation;
                m_HasObservation = false;
            }

            LastObservation = observation;
            ApplyObservation(observation.Gesture);
            MaybeLogDebugFeatures(observation);
        }

        void ApplyObservation(RgbHandGesture raw)
        {
            if (raw == m_Candidate)
                m_CandidateCount++;
            else
            {
                m_Candidate = raw;
                m_CandidateCount = 1;
            }

            var needed = m_Candidate == RgbHandGesture.None ? Mathf.Max(1, m_NoneConfirmFrames) : Mathf.Max(1, m_ConfirmFrames);
            if (m_CandidateCount >= needed && m_Candidate != CurrentGesture)
                SetCurrentGesture(m_Candidate);
        }

        void SetCurrentGesture(RgbHandGesture gesture)
        {
            var previous = CurrentGesture;
            CurrentGesture = gesture;
            GestureChanged?.Invoke(gesture);
            GestureChangedDetailed?.Invoke(this, new RgbHandGestureChangedEventArgs(previous, gesture));
            AnyGestureChanged?.Invoke(gesture);

            LogStatus($"识别到手势: {RgbHandGestureNames.ToChinese(gesture)}");
        }

        void MaybeLogDebugFeatures(RgbHandGestureObservation observation)
        {
            if (!m_LogDebugFeatures || Time.realtimeSinceStartup < m_NextDebugLogTime)
                return;

            m_NextDebugLogTime = Time.realtimeSinceStartup + DebugFeatureLogIntervalSeconds;
            var hole = observation.HasHole ? "有孔" : "无孔";
            BeamProUnifiedLogWindow.AddLine(
                LogSource,
                $"原始={RgbHandGestureNames.ToChinese(observation.Gesture)} 缺陷={observation.DefectCount} 峰={observation.PeakCount} 紧致={observation.Compactness:0.00} 面积={observation.AreaRatio * 100f:0.0}% {hole}");
        }

        void PublishStatus()
        {
            BeamProUnifiedLogWindow.SetStatus(LogSource, BuildStatusText());
        }

        string BuildStatusText()
        {
            var enabled = m_RecognitionEnabled ? "开" : "关";
            var gesture = RgbHandGestureNames.ToChinese(CurrentGesture);
            var obs = LastObservation;
            if (!obs.HandDetected)
                return $"识别: {enabled} | 手势: {gesture} | 未检测到手";

            return $"识别: {enabled} | 手势: {gesture}\n缺陷={obs.DefectCount} 峰={obs.PeakCount} 紧致={obs.Compactness:0.00} 面积={obs.AreaRatio * 100f:0.0}%";
        }

        void LogStatus(string message, bool warning = false)
        {
            BeamProUnifiedLogWindow.SetStatus(LogSource, BuildStatusText());
            BeamProUnifiedLogWindow.AddLine(LogSource, message);

            var unityLog = $"RgbHandGestureRecognizer: {message}";
            if (warning)
                Debug.LogWarning(unityLog);
            else
                Debug.Log(unityLog);
        }
    }
}
