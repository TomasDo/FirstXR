using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Encodes one fixed-layout 1280x720 stream: actual XR left-eye output on the left,
    /// optional Eye RGB on the right. It owns the application's sole VideoEncoder and
    /// never starts/stops XREALRGBCameraTexture directly.
    /// </summary>
    public sealed class XrRgbRtpStreamer : MonoBehaviour
    {
        [SerializeField] string m_DestinationHost = "192.168.31.166";
        [SerializeField] int m_DestinationPort = 5555;
        [SerializeField] int m_OutputWidth = 1280;
        [SerializeField] int m_OutputHeight = 720;
        [SerializeField] int m_FrameRate = 15;
        [SerializeField] bool m_StreamOnStart;
        [SerializeField] bool m_IncludeRgbOnStart;

        static XrRgbRtpStreamer s_Instance;

        LeftEyeDisplayWindow m_LeftEye;
        RgbCameraFrameService m_CameraService;
        VideoEncoder m_Encoder;
        RenderTexture m_CompositeTexture;
        Material m_YuvMaterial;
        bool m_StreamingRequested;
        bool m_IncludeXr = true;
        bool m_IncludeRgb;
        bool m_HasRgbLease;
        double m_NextFrameTime;
        ulong m_LastTimestamp;
        string m_Status = "RTP stopped";
        string m_FaultMessage = string.Empty;
        bool m_XrUnavailableReported;

        public static XrRgbRtpStreamer Instance => s_Instance;
        public bool IsStreaming => m_Encoder != null;
        public bool IncludeXr => m_IncludeXr;
        public bool IncludeRgb => m_IncludeRgb;
        public string DestinationUri => $"rtp://{m_DestinationHost}:{m_DestinationPort}";
        public string Status => m_Status;
        public string FaultMessage => m_FaultMessage;
        public bool IsHealthy => m_Encoder != null && string.IsNullOrEmpty(m_FaultMessage);
        public RenderTexture CompositeTexture => m_CompositeTexture;
        public bool IsRuntimeSupported => Application.platform == RuntimePlatform.Android && !Application.isEditor;
        public bool HasLiveXrFrame => m_LeftEye != null && m_LeftEye.HasLiveXrFrame;
        public bool HasLiveRgbFrame => m_CameraService != null
            && m_CameraService.TryGetLatestFrame(out var frame)
            && frame.IsValid
            && Time.realtimeSinceStartupAsDouble - frame.ReceivedAtSeconds <= 0.25;
        public string SourceAvailabilityMessage
        {
            get
            {
                if (!string.IsNullOrEmpty(m_FaultMessage))
                    return m_FaultMessage;
                if (m_IncludeXr && !HasLiveXrFrame)
                    return "actual XR left-eye output is not currently available";
                if (m_IncludeRgb && !HasLiveRgbFrame)
                    return "RGB camera frame is not currently available";
                return string.Empty;
            }
        }

        public event Action<string> StatusChanged;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
            s_Instance = this;
            m_LeftEye = GetComponent<LeftEyeDisplayWindow>();
        }

        void Start()
        {
            m_CameraService = RgbCameraFrameService.EnsureInstance();
            if (m_LeftEye == null)
                m_LeftEye = FindObjectOfType<LeftEyeDisplayWindow>();
            BindLeftEye();

            m_IncludeRgb = m_IncludeRgbOnStart;
            if (m_StreamOnStart)
                SetStreamingEnabled(true);
        }

        void OnEnable()
        {
            BindLeftEye();
            if (m_StreamingRequested)
            {
                StartEncoder();
                ApplyRgbLease();
            }
        }

        void OnDisable()
        {
            UnbindLeftEye();
            StopEncoder();
            ReleaseRgbLease();
        }

        void OnDestroy()
        {
            UnbindLeftEye();
            StopEncoder();
            ReleaseRgbLease();
            ReleaseGraphicsResources();
            if (s_Instance == this)
                s_Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                StopEncoder();
                ReleaseRgbLease();
            }
            else if (m_StreamingRequested)
            {
                StartEncoder();
                ApplyRgbLease();
            }
        }

        void Update()
        {
            if (m_Encoder == null)
                return;

            // A live XR source clocks the composite in its callback. RGB-only mode and missing
            // XR output use this timer so an RGB request remains independently usable.
            if (m_IncludeXr && m_LeftEye != null && m_LeftEye.HasLiveXrFrame)
                return;

            var now = Time.realtimeSinceStartupAsDouble;
            if (m_IncludeXr && !m_XrUnavailableReported)
            {
                m_XrUnavailableReported = true;
                SetStatus("RTP active, but actual XR left-eye output is unavailable", true);
            }

            // Keep RTP alive with a black XR pane instead of repeating a stale surgical view.
            if (now >= m_NextFrameTime)
                CommitCompositeFrame(null, now);
        }

        public bool ConfigureDestination(string host, int port)
        {
            if (!DestinationIsValid(host, port))
                return false;

            host = host.Trim();
            if (host.Contains("://") || host.Contains("/") || host.Contains(" "))
                return false;

            var changed = host != m_DestinationHost || port != m_DestinationPort;
            m_DestinationHost = host;
            m_DestinationPort = port;
            if (changed && IsStreaming)
            {
                StopEncoder();
                return StartEncoder();
            }
            return true;
        }

        /// <summary>Applies the negotiated composite size/rate. Zero keeps the configured default.</summary>
        public bool ConfigureFormat(int width, int height, int framesPerSecond)
        {
            width = width == 0 ? m_OutputWidth : width;
            height = height == 0 ? m_OutputHeight : height;
            framesPerSecond = framesPerSecond == 0 ? m_FrameRate : framesPerSecond;
            if (!FormatIsValid(width, height, framesPerSecond))
            {
                SetStatus("RTP format rejected: use even 320..3840 x 180..2160 at 1..30 fps", true);
                return false;
            }

            var changed = width != m_OutputWidth || height != m_OutputHeight || framesPerSecond != m_FrameRate;
            m_OutputWidth = width;
            m_OutputHeight = height;
            m_FrameRate = framesPerSecond;
            if (changed && IsStreaming)
            {
                StopEncoder();
                return StartEncoder();
            }
            return true;
        }

        /// <summary>
        /// Applies one server control as a transaction. If the requested encoder cannot start,
        /// the previous configuration and requested running state are restored before returning.
        /// </summary>
        public bool TryApplyControl(DentalObservationControlState control, out string error)
        {
            error = string.Empty;
            var streamRequested = control.MirrorEnabled || control.RgbEnabled;
            var nextWidth = control.Width == 0 ? m_OutputWidth : control.Width;
            var nextHeight = control.Height == 0 ? m_OutputHeight : control.Height;
            var nextFps = control.Fps == 0 ? m_FrameRate : control.Fps;
            var nextHost = string.IsNullOrWhiteSpace(control.ReceiverHost)
                ? (streamRequested ? string.Empty : m_DestinationHost)
                : control.ReceiverHost.Trim();
            var nextPort = control.ReceiverPort == 0 && !streamRequested
                ? m_DestinationPort
                : control.ReceiverPort;

            if (streamRequested && !DestinationIsValid(nextHost, nextPort))
            {
                error = "RTP destination rejected: provide a host and port 1..65535";
                return false;
            }
            if (streamRequested && !FormatIsValid(nextWidth, nextHeight, nextFps))
            {
                error = "RTP format rejected: use even 320..3840 x 180..2160 at 1..30 fps";
                return false;
            }

            var previousHost = m_DestinationHost;
            var previousPort = m_DestinationPort;
            var previousWidth = m_OutputWidth;
            var previousHeight = m_OutputHeight;
            var previousFps = m_FrameRate;
            var previousIncludeXr = m_IncludeXr;
            var previousIncludeRgb = m_IncludeRgb;
            var previousRequested = m_StreamingRequested;

            StopEncoder();
            ReleaseRgbLease();
            m_DestinationHost = nextHost;
            m_DestinationPort = nextPort;
            m_OutputWidth = nextWidth;
            m_OutputHeight = nextHeight;
            m_FrameRate = nextFps;
            m_IncludeXr = control.MirrorEnabled;
            m_IncludeRgb = control.RgbEnabled;
            m_StreamingRequested = streamRequested;
            m_FaultMessage = string.Empty;

            if (!streamRequested)
            {
                SetStatus("RTP stopped");
                return true;
            }

            if (StartEncoder())
            {
                ApplyRgbLease();
                return true;
            }

            error = string.IsNullOrEmpty(m_FaultMessage) ? m_Status : m_FaultMessage;
            if (string.IsNullOrEmpty(error))
                error = "RTP encoder did not start";

            StopEncoder();
            ReleaseRgbLease();
            m_DestinationHost = previousHost;
            m_DestinationPort = previousPort;
            m_OutputWidth = previousWidth;
            m_OutputHeight = previousHeight;
            m_FrameRate = previousFps;
            m_IncludeXr = previousIncludeXr;
            m_IncludeRgb = previousIncludeRgb;
            m_StreamingRequested = previousRequested;
            m_FaultMessage = string.Empty;
            if (previousRequested && !StartEncoder())
                error += "; previous RTP stream could not be restored: " + m_Status;
            ApplyRgbLease();
            return false;
        }

        /// <summary>Navigation-side control hook. Streaming is off by default.</summary>
        public bool SetStreamingEnabled(bool enabled)
        {
            m_StreamingRequested = enabled;
            if (!enabled)
            {
                StopEncoder();
                ReleaseRgbLease();
                return true;
            }

            var started = StartEncoder();
            ApplyRgbLease();
            return started;
        }

        /// <summary>
        /// Navigation-side control hook. Disabling RGB releases only this consumer's lease;
        /// gesture recognition can keep the shared camera running.
        /// </summary>
        public void SetRgbViewEnabled(bool enabled)
        {
            m_IncludeRgb = enabled;
            ApplyRgbLease();
            SetStatus(enabled ? "RGB pane requested" : "RGB pane disabled; gesture camera lease unchanged");
        }

        public void SetXrMirrorEnabled(bool enabled)
        {
            m_IncludeXr = enabled;
            SetStatus(enabled ? "XR left-eye pane requested" : "XR left-eye pane disabled");
        }

        bool StartEncoder()
        {
            if (m_Encoder != null)
                return true;

            if (!IsRuntimeSupported)
            {
                SetStatus("RTP unavailable: XREAL VideoEncoder requires an Android player", true);
                return false;
            }

            try
            {
                EnsureCompositeTexture();
                var parameters = new CameraParameters(CamMode.VideoMode, BlendMode.VirtualOnly)
                {
                    cameraType = CameraType.RGB,
                    hologramOpacity = 1f,
                    frameRate = Mathf.Clamp(m_FrameRate, 1, 30),
                    cameraResolutionWidth = Mathf.Max(64, m_OutputWidth),
                    cameraResolutionHeight = Mathf.Max(64, m_OutputHeight),
                    pixelFormat = CapturePixelFormat.BGRA32,
                    audioState = AudioState.None,
                    captureSide = CaptureSide.Single,
                    monophonic = true,
                    backgroundColor = Color.black,
                };

                m_Encoder = new VideoEncoder();
                m_Encoder.Config(parameters);
                m_Encoder.EncodeConfig.SetOutPutPath(DestinationUri);
                m_Encoder.Start();
                m_NextFrameTime = 0;
                m_LastTimestamp = 0;
                m_XrUnavailableReported = false;
                m_FaultMessage = string.Empty;
                SetStatus($"RTP streaming {m_OutputWidth}x{m_OutputHeight}@{m_FrameRate} to {DestinationUri}");
                return true;
            }
            catch (Exception ex)
            {
                StopEncoder();
                SetStatus($"RTP start failed: {ex.Message}", true);
                return false;
            }
        }

        void StopEncoder()
        {
            var encoder = m_Encoder;
            if (encoder == null)
            {
                SetStatus("RTP stopped");
                return;
            }

            m_Encoder = null;
            try
            {
                encoder.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"XrRgbRtpStreamer: encoder stop failed: {ex.Message}");
            }
            try
            {
                encoder.Release();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"XrRgbRtpStreamer: encoder release failed: {ex.Message}");
            }
            SetStatus("RTP stopped");
        }

        void BindLeftEye()
        {
            if (m_LeftEye == null)
                m_LeftEye = FindObjectOfType<LeftEyeDisplayWindow>();
            if (m_LeftEye == null)
                return;
            m_LeftEye.XrFrameUpdated -= OnXrFrame;
            m_LeftEye.XrFrameUpdated += OnXrFrame;
        }

        void UnbindLeftEye()
        {
            if (m_LeftEye != null)
                m_LeftEye.XrFrameUpdated -= OnXrFrame;
        }

        void OnXrFrame(RenderTexture xrTexture)
        {
            if (m_Encoder == null || xrTexture == null)
                return;

            var now = Time.realtimeSinceStartupAsDouble;
            if (now < m_NextFrameTime)
                return;

            if (m_XrUnavailableReported)
            {
                m_XrUnavailableReported = false;
                SetStatus($"RTP streaming {m_OutputWidth}x{m_OutputHeight}@{m_FrameRate} to {DestinationUri}");
            }

            CommitCompositeFrame(xrTexture, now);
        }

        void CommitCompositeFrame(Texture xrTexture, double now)
        {
            m_NextFrameTime = now + 1.0 / Mathf.Clamp(m_FrameRate, 1, 30);
            try
            {
                Compose(xrTexture);
                var timestamp = (ulong)Math.Max(0, Math.Round(now * 1000.0));
                if (timestamp <= m_LastTimestamp)
                    timestamp = m_LastTimestamp + 1;
                m_LastTimestamp = timestamp;
                m_Encoder.Commit(m_CompositeTexture, timestamp);
            }
            catch (Exception ex)
            {
                var fault = $"RTP frame failed: {ex.Message}";
                StopEncoder();
                ReleaseRgbLease();
                m_FaultMessage = fault;
                SetStatus(fault, true);
            }
        }

        static bool DestinationIsValid(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
                return false;
            host = host.Trim();
            return !host.Contains("://") && !host.Contains("/") && !host.Contains(" ");
        }

        static bool FormatIsValid(int width, int height, int framesPerSecond)
        {
            return width >= 320 && width <= 3840
                && height >= 180 && height <= 2160
                && (width & 1) == 0 && (height & 1) == 0
                && framesPerSecond >= 1 && framesPerSecond <= 30;
        }

        void Compose(Texture xrTexture)
        {
            EnsureCompositeTexture();
            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(m_CompositeTexture);
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, m_OutputWidth, m_OutputHeight, 0);
                GL.Clear(true, true, Color.black);

                if (m_IncludeXr && xrTexture != null)
                {
                    var leftPane = new Rect(0, 0, m_OutputWidth * 0.5f, m_OutputHeight);
                    Graphics.DrawTexture(AspectFit(xrTexture, leftPane), xrTexture);
                }

                if (m_IncludeRgb && m_CameraService != null
                    && m_CameraService.TryGetLatestFrame(out var rgbFrame) && rgbFrame.IsValid)
                {
                    EnsureYuvMaterial();
                    if (m_YuvMaterial != null)
                    {
                        m_YuvMaterial.SetTexture("_MainTex", rgbFrame.Y);
                        m_YuvMaterial.SetTexture("_UTex", rgbFrame.U);
                        m_YuvMaterial.SetTexture("_VTex", rgbFrame.V);
                        var rightPane = new Rect(m_OutputWidth * 0.5f, 0, m_OutputWidth * 0.5f, m_OutputHeight);
                        Graphics.DrawTexture(AspectFit(rgbFrame.Y, rightPane), rgbFrame.Y, m_YuvMaterial);
                    }
                }
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        static Rect AspectFit(Texture texture, Rect bounds)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return bounds;
            var sourceAspect = (float)texture.width / texture.height;
            var boundsAspect = bounds.width / bounds.height;
            if (sourceAspect > boundsAspect)
            {
                var height = bounds.width / sourceAspect;
                return new Rect(bounds.x, bounds.y + (bounds.height - height) * 0.5f, bounds.width, height);
            }
            var width = bounds.height * sourceAspect;
            return new Rect(bounds.x + (bounds.width - width) * 0.5f, bounds.y, width, bounds.height);
        }

        void EnsureCompositeTexture()
        {
            var width = Mathf.Max(64, m_OutputWidth);
            var height = Mathf.Max(64, m_OutputHeight);
            if (m_CompositeTexture != null && m_CompositeTexture.width == width && m_CompositeTexture.height == height)
                return;
            if (m_CompositeTexture != null)
            {
                m_CompositeTexture.Release();
                Destroy(m_CompositeTexture);
            }
            m_CompositeTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "XR RGB Observation Composite",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_CompositeTexture.Create();
        }

        void EnsureYuvMaterial()
        {
            if (m_YuvMaterial != null)
                return;
            var window = FindObjectOfType<RGBCameraFloatingWindow>();
            m_YuvMaterial = window != null ? window.CreateYuvMaterialInstance() : null;
            if (m_YuvMaterial == null)
            {
                var shader = Shader.Find("Unlit/YUVTransRGB");
                if (shader != null)
                    m_YuvMaterial = new Material(shader);
            }
        }

        void ApplyRgbLease()
        {
            var shouldHold = m_StreamingRequested && m_Encoder != null && m_IncludeRgb;
            if (shouldHold == m_HasRgbLease)
                return;
            if (m_CameraService == null)
                m_CameraService = RgbCameraFrameService.EnsureInstance();
            m_HasRgbLease = shouldHold;
            if (shouldHold)
                m_CameraService.AcquireCapture(this);
            else
                m_CameraService.ReleaseCapture(this);
        }

        void ReleaseRgbLease()
        {
            if (!m_HasRgbLease || m_CameraService == null)
                return;
            m_HasRgbLease = false;
            m_CameraService.ReleaseCapture(this);
        }

        void ReleaseGraphicsResources()
        {
            if (m_CompositeTexture != null)
            {
                m_CompositeTexture.Release();
                Destroy(m_CompositeTexture);
                m_CompositeTexture = null;
            }
            if (m_YuvMaterial != null)
            {
                Destroy(m_YuvMaterial);
                m_YuvMaterial = null;
            }
        }

        void SetStatus(string message, bool warning = false)
        {
            m_Status = message;
            StatusChanged?.Invoke(message);
            BeamProUnifiedLogWindow.SetStatus("远端观察", message);
            if (warning)
                Debug.LogWarning($"XrRgbRtpStreamer: {message}");
            else
                Debug.Log($"XrRgbRtpStreamer: {message}");
        }
    }
}
