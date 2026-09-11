using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Unity.XR.XREAL;
using static UnityEngine.XR.XRDisplaySubsystem;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Mirrors the left-eye XR display output to the Beam Pro phone screen.
    /// </summary>
    public class LeftEyeDisplayWindow : MonoBehaviour
    {
        static readonly int s_SliceProperty = Shader.PropertyToID("_Slice");

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
        string m_StatusMessage = "Initializing left eye preview...";
        string m_DebugInfo;

        void Start()
        {
            m_ArraySliceMaterial = CreateArraySliceMaterial();
            StartCoroutine(InitializeAndCaptureLoop());
        }

        void OnDisable()
        {
            StopAllCoroutines();
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
                m_StatusMessage = "XRDisplaySubsystem not available.";

            while (enabled)
            {
                yield return new WaitForEndOfFrame();
                UpdatePreviewTexture();
            }
        }

        void UpdatePreviewTexture()
        {
            if (m_DisplaySubsystem == null)
                m_DisplaySubsystem = XREALUtility.GetLoadedSubsystem<XRDisplaySubsystem>();

            if (m_DisplaySubsystem == null)
                return;

            if (TryBlitLeftEyeFromXrDisplay(m_DisplaySubsystem))
                return;

            if (m_FallbackToMainCameraRender && TryRenderMainCameraPreview())
                return;

            m_DebugInfo = "No left eye frame available.";
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
                m_DebugInfo = $"XR array slice {renderParameter.textureArraySlice}, {targetWidth}x{targetHeight}";
                return true;
            }

            EnsurePreviewTexture(targetWidth, targetHeight);

            if (viewport.width > 1f && viewport.height > 1f)
            {
                var scale = new Vector2(viewport.width / source.width, viewport.height / source.height);
                var offset = new Vector2(viewport.x / source.width, viewport.y / source.height);
                Graphics.Blit(source, m_PreviewTexture, scale, offset);
                m_DebugInfo = $"XR viewport blit {viewport.width:F0}x{viewport.height:F0}";
                return true;
            }

            if (source.width >= 2)
            {
                var scale = new Vector2(0.5f, 1f);
                var offset = m_LeftEyeIndex == 0 ? Vector2.zero : new Vector2(0.5f, 0f);
                Graphics.Blit(source, m_PreviewTexture, scale, offset);
                m_DebugInfo = $"XR side-by-side blit (eye {m_LeftEyeIndex})";
                return true;
            }

            Graphics.Blit(source, m_PreviewTexture);
            m_DebugInfo = $"XR full blit {source.width}x{source.height}";
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

            m_DebugInfo = $"Main camera fallback {width}x{height}";
            return true;
        }

        void EnsurePreviewTexture(int width, int height)
        {
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);

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
            var shader = Shader.Find("Hidden/XREAL/BlitTextureArraySlice");
            return shader != null ? new Material(shader) : null;
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
            GUI.Label(new Rect(x, y, previewWidth, headerHeight), "Left Eye (One Pro view)", m_HeaderStyle);

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
