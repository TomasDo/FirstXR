using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Unity.XR.XREAL;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Mirrors the left-eye XR display output to the Beam Pro phone screen.
    /// </summary>
    public class LeftEyeDisplayWindow : MonoBehaviour
    {
        static readonly int s_SliceProperty = Shader.PropertyToID("_Slice");
        static readonly WaitForEndOfFrame s_EndOfFrame = new WaitForEndOfFrame();

        enum CaptureMode { Warmup, Preview, Stream }

        const double PreviewIntervalSeconds = 1.0 / 30.0;
        // Remote XR-only stream startup requires a real frame younger than 250 ms.
        // Keep a small, fresh frame even while the phone is on another page.
        const double WarmupIntervalSeconds = 0.1;

        [SerializeField]
        bool m_ShowOnBeamPro = true;

        [SerializeField]
        int m_LeftEyeIndex = 0;

        [SerializeField]
        float m_MaxScreenHeightFraction = 0.45f;

        [SerializeField]
        bool m_FallbackToMainCameraRender = true;

        XRDisplaySubsystem m_DisplaySubsystem;
        RenderTexture m_PreviewTexture;
        Material m_ArraySliceMaterial;
        string m_StatusMessage = "Initializing XR left-eye preview…";
        string m_DebugInfo;
        bool m_LastFrameIsXrDisplay;
        bool m_LoggedFirstXrFrame;
        double m_LastXrFrameTime;
        double m_NextCaptureTime;
        CaptureMode m_CaptureMode;
        bool m_HaveCaptureMode;
        int m_DebugSourceKind = -1;
        int m_DebugSlice;
        int m_DebugWidth;
        int m_DebugHeight;

        public RenderTexture PreviewTexture => m_PreviewTexture;
        public string PreviewStatusMessage => m_StatusMessage;
        public string PreviewDebugInfo => m_DebugInfo;
        public bool HasLiveXrFrame => m_LastFrameIsXrDisplay
            && Time.realtimeSinceStartupAsDouble - m_LastXrFrameTime <= 0.25;
        public event Action<RenderTexture> XrFrameUpdated;

        void Awake()
        {
            EnsureObservationStreamer();
        }

        void Start()
        {
            m_ArraySliceMaterial = CreateArraySliceMaterial();
            StartCoroutine(InitializeAndCaptureLoop());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            m_LastFrameIsXrDisplay = false;
            m_HaveCaptureMode = false;
        }

        void OnEnable()
        {
            if (m_ArraySliceMaterial != null)
                StartCoroutine(InitializeAndCaptureLoop());
        }

        void OnDestroy()
        {
            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                Destroy(m_PreviewTexture);
            }

            if (m_ArraySliceMaterial != null)
                Destroy(m_ArraySliceMaterial);
        }

        IEnumerator InitializeAndCaptureLoop()
        {
            for (var i = 0; i < 300 && m_DisplaySubsystem == null; i++)
            {
                m_DisplaySubsystem = XREALUtility.GetLoadedSubsystem<XRDisplaySubsystem>();
                if (m_DisplaySubsystem != null)
                {
                    m_StatusMessage = null;
                    break;
                }

                yield return null;
            }

            if (m_DisplaySubsystem == null)
                m_StatusMessage = "XR left-eye view unavailable.";

            while (enabled)
            {
                yield return s_EndOfFrame;
                var previewVisible = BeamProPagedController.IsActive
                    ? BeamProPagedController.IsLeftEyePreviewVisible
                    : m_ShowOnBeamPro && Application.platform == RuntimePlatform.Android;
                // This streamer is currently the only XrFrameUpdated consumer. It remains
                // subscribed while stopped, so subscription alone is not capture demand.
                var streamer = XrRgbRtpStreamer.Instance;
                var streaming = streamer != null && streamer.IsStreaming && streamer.IncludeXr;
                if (TryBeginCapture(Time.realtimeSinceStartupAsDouble, previewVisible, streaming))
                    UpdatePreviewTexture();
            }
        }

        bool TryBeginCapture(double now, bool previewVisible, bool streaming)
        {
            var mode = streaming ? CaptureMode.Stream
                : previewVisible ? CaptureMode.Preview : CaptureMode.Warmup;
            if (m_HaveCaptureMode && m_CaptureMode == mode && now + 0.000001 < m_NextCaptureTime)
                return false;

            m_CaptureMode = mode;
            m_HaveCaptureMode = true;
            m_NextCaptureTime = now + (mode == CaptureMode.Stream ? 0.0
                : mode == CaptureMode.Preview ? PreviewIntervalSeconds : WarmupIntervalSeconds);
            return true;
        }

        void UpdatePreviewTexture()
        {
            if (m_DisplaySubsystem == null)
                m_DisplaySubsystem = XREALUtility.GetLoadedSubsystem<XRDisplaySubsystem>();

            if (m_DisplaySubsystem == null)
                return;

            if (TryBlitLeftEyeFromXrDisplay(m_DisplaySubsystem))
            {
                m_LastFrameIsXrDisplay = true;
                m_LastXrFrameTime = Time.realtimeSinceStartupAsDouble;
                if (!m_LoggedFirstXrFrame)
                {
                    m_LoggedFirstXrFrame = true;
                    Debug.Log($"LeftEyePreview: first live frame ({m_DebugInfo}).");
                }
                XrFrameUpdated?.Invoke(m_PreviewTexture);
                return;
            }

            if (m_FallbackToMainCameraRender
                && m_CaptureMode == CaptureMode.Preview
                && !BeamProPagedController.IsActive
                && TryRenderMainCameraPreview())
            {
                m_LastFrameIsXrDisplay = false;
                return;
            }

            m_LastFrameIsXrDisplay = false;
            m_DebugSourceKind = -1;
            m_DebugInfo = "No XR left-eye frame available.";
        }

        public bool TryGetLiveXrFrame(out RenderTexture texture)
        {
            texture = HasLiveXrFrame ? m_PreviewTexture : null;
            return texture != null;
        }

        void EnsureObservationStreamer()
        {
            if (FindFirstObjectByType<XrRgbRtpStreamer>() == null)
                gameObject.AddComponent<XrRgbRtpStreamer>();
        }

        bool TryBlitLeftEyeFromXrDisplay(XRDisplaySubsystem display)
        {
            var passCount = display.GetRenderPassCount();
            if (passCount == 0)
                return false;

            var camera = XREALUtility.MainCamera;
            if (camera == null)
                return false;

            var passIndex = passCount == 2 && m_LeftEyeIndex == 1 ? 1 : 0;
            display.GetRenderPass(passIndex, out var renderPass);

            var parameterCount = renderPass.GetRenderParameterCount();
            if (parameterCount == 0)
                return false;

            var parameterIndex = parameterCount == 2 && m_LeftEyeIndex == 1 ? 1 : 0;
            renderPass.GetRenderParameter(camera, parameterIndex, out var renderParameter);

            var source = display.GetRenderTextureForRenderPass(passIndex);
            if (source == null)
                return false;

            var viewport = renderParameter.viewport;
            var targetWidth = viewport.width > 1f ? Mathf.RoundToInt(viewport.width) : source.width;
            var targetHeight = viewport.height > 1f ? Mathf.RoundToInt(viewport.height) : source.height;

            if (source.dimension == TextureDimension.Tex2DArray && source.volumeDepth > 1)
            {
                EnsurePreviewTexture(targetWidth, targetHeight);
                if (m_ArraySliceMaterial == null)
                    return false;

                m_ArraySliceMaterial.SetInt(s_SliceProperty, renderParameter.textureArraySlice);
                Graphics.Blit(source, m_PreviewTexture, m_ArraySliceMaterial);
                SetDebugInfo(0, renderParameter.textureArraySlice);
                return true;
            }

            EnsurePreviewTexture(targetWidth, targetHeight);

            if (viewport.width > 1f && viewport.height > 1f)
            {
                var scale = new Vector2(viewport.width / source.width, viewport.height / source.height);
                var offset = new Vector2(viewport.x / source.width, viewport.y / source.height);
                Graphics.Blit(source, m_PreviewTexture, scale, offset);
                SetDebugInfo(1);
                return true;
            }

            if (source.width >= 2)
            {
                var scale = new Vector2(0.5f, 1f);
                var offset = m_LeftEyeIndex == 0 ? Vector2.zero : new Vector2(0.5f, 0f);
                Graphics.Blit(source, m_PreviewTexture, scale, offset);
                SetDebugInfo(2, m_LeftEyeIndex);
                return true;
            }

            Graphics.Blit(source, m_PreviewTexture);
            SetDebugInfo(3);
            return true;
        }

        bool TryRenderMainCameraPreview()
        {
            var camera = XREALUtility.MainCamera;
            if (camera == null)
                return false;

            var width = Mathf.Max(64, XRSettings.eyeTextureWidth > 0 ? XRSettings.eyeTextureWidth : 1280);
            var height = Mathf.Max(64, XRSettings.eyeTextureHeight > 0 ? XRSettings.eyeTextureHeight : 720);
            EnsurePreviewTexture(width, height);

            var previousTarget = camera.targetTexture;
            var previousEnabled = camera.enabled;
            try
            {
                camera.targetTexture = m_PreviewTexture;
                camera.enabled = true;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.enabled = previousEnabled;
            }

            SetDebugInfo(4);
            return true;
        }

        void SetDebugInfo(int sourceKind, int slice = 0)
        {
            var width = m_PreviewTexture.width;
            var height = m_PreviewTexture.height;
            if (m_DebugSourceKind == sourceKind && m_DebugSlice == slice
                && m_DebugWidth == width && m_DebugHeight == height)
                return;

            m_DebugSourceKind = sourceKind;
            m_DebugSlice = slice;
            m_DebugWidth = width;
            m_DebugHeight = height;
            switch (sourceKind)
            {
                case 0: m_DebugInfo = $"XR array slice {slice}, {width}x{height}"; break;
                case 1: m_DebugInfo = $"XR viewport blit {width}x{height}"; break;
                case 2: m_DebugInfo = $"XR side-by-side blit (eye {slice}), {width}x{height}"; break;
                case 3: m_DebugInfo = $"XR full blit {width}x{height}"; break;
                default: m_DebugInfo = $"Main camera fallback {width}x{height} (not streamable XR output)"; break;
            }
        }

        static Vector2Int GetCaptureSize(int width, int height, bool previewVisible, bool streaming)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (streaming)
                return new Vector2Int(width, height);

            var maxWidth = previewVisible ? 1280f : 320f;
            var maxHeight = previewVisible ? 720f : 180f;
            var scale = Mathf.Min(1f, Mathf.Min(maxWidth / width, maxHeight / height));
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }

        void EnsurePreviewTexture(int width, int height)
        {
            var size = GetCaptureSize(width, height,
                m_CaptureMode == CaptureMode.Preview, m_CaptureMode == CaptureMode.Stream);
            width = size.x;
            height = size.y;

            if (m_PreviewTexture != null
                && m_PreviewTexture.width == width
                && m_PreviewTexture.height == height)
                return;

            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                Destroy(m_PreviewTexture);
            }

            m_PreviewTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            m_PreviewTexture.Create();
        }

        static Material CreateArraySliceMaterial()
        {
            // Shader.Find does not retain an otherwise unreferenced shader in a player.
            // Keep this shader listed in GraphicsSettings.AlwaysIncludedShaders.
            var shader = Shader.Find("Hidden/XREAL/BlitTextureArraySlice");
            if (shader == null)
            {
                Debug.LogError("LeftEyePreview: array slice shader is missing from the player; XR preview is unavailable.");
                return null;
            }

            if (!shader.isSupported)
            {
                Debug.LogError("LeftEyePreview: array slice shader is unsupported on this graphics device; XR preview is unavailable.");
                return null;
            }

            return new Material(shader);
        }

        GUIStyle m_HeaderStyle;
        GUIStyle m_InfoStyle;
        GUIStyle m_MessageStyle;

        void EnsureGuiStyles()
        {
            var headerHeight = Mathf.Max(28f, Screen.height / 48f);
            if (m_HeaderStyle != null && m_HeaderStyle.fontSize == Mathf.Max(14, (int)(headerHeight * 0.45f)))
                return;

            m_HeaderStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(14, (int)(headerHeight * 0.45f)),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            m_InfoStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerRight,
                fontSize = Mathf.Max(12, Screen.height / 72),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f, 0.9f) }
            };
            m_MessageStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(14, Screen.height / 64),
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }

        void OnGUI()
        {
            if (!m_ShowOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            if (BeamProPagedController.IsActive)
                return;

            var texture = m_PreviewTexture;
            var headerHeight = Mathf.Max(28f, Screen.height / 48f);
            var region = BeamProOverlayLayout.GetBottomPreviewRect(
                BeamProOverlayLayout.MaxButtonRows,
                m_MaxScreenHeightFraction);

            const float panelPadding = 8f;
            var maxPreviewWidth = Mathf.Max(120f, region.width - panelPadding * 2f);
            var maxPreviewHeight = Mathf.Max(80f, region.height - headerHeight - panelPadding * 2f);

            float previewWidth = maxPreviewWidth;
            float previewHeight = maxPreviewHeight;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                var aspect = (float)texture.width / texture.height;
                previewHeight = Mathf.Min(maxPreviewHeight, previewWidth / aspect);
                previewWidth = previewHeight * aspect;
            }

            var totalBlockHeight = headerHeight + previewHeight;
            var x = region.x + (region.width - previewWidth) * 0.5f;
            var y = region.y + (region.height - totalBlockHeight) * 0.5f;
            var panelRect = new Rect(
                region.x,
                region.y,
                region.width,
                region.height);

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = previousColor;

            EnsureGuiStyles();
            var title = HasLiveXrFrame ? "Left Eye (actual XR output)" : "Left Eye (XR output unavailable)";
            GUI.Label(new Rect(x, y, previewWidth, headerHeight), title, m_HeaderStyle);

            var previewRect = new Rect(x, y + headerHeight, previewWidth, previewHeight);
            if (texture != null)
            {
                GUI.DrawTexture(previewRect, texture, ScaleMode.ScaleToFit, true);
                if (!string.IsNullOrEmpty(m_DebugInfo))
                    GUI.Label(previewRect, m_DebugInfo, m_InfoStyle);
            }
            else
            {
                var message = string.IsNullOrEmpty(m_StatusMessage)
                    ? "Waiting for left eye frame..."
                    : m_StatusMessage;
                GUI.Label(previewRect, message, m_MessageStyle);
            }
        }
    }
}
