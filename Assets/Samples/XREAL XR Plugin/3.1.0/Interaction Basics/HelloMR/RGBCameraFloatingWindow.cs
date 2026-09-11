using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Optional world-space RGB preview. Camera lifetime is owned by
    /// <see cref="RgbCameraFrameService"/> and shared with gestures/streaming.
    /// </summary>
    public class RGBCameraFloatingWindow : MonoBehaviour
    {
        [SerializeField] float m_DistanceMeters = 2f;
        [SerializeField] float m_VerticalOffsetMeters = 0.35f;
        [SerializeField] float m_HorizontalOffsetMeters = -0.55f;
        [SerializeField] float m_WidthMeters = 1.26f;
        [SerializeField] float m_AspectRatio = 16f / 9f;
        [SerializeField] bool m_StartCaptureOnAwake = false;
        [SerializeField] Material m_YuvMaterialTemplate;
        [SerializeField] bool m_ShowDebugOnBeamPro = true;
        [SerializeField] bool m_EnableOfflineGesturePipeline = true;

        RgbCameraFrameService m_CameraService;
        GameObject m_WindowRoot;
        Material m_PreviewMaterial;
        Material m_FrameMaterial;
        bool m_WindowVisible;
        bool m_LegacyCaptureLease;
        bool m_ReceivedFirstCameraFrame;

        void Awake()
        {
            m_CameraService = RgbCameraFrameService.EnsureInstance(gameObject);
            EnsureGesturePipeline();
        }

        void Start()
        {
            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            if (camera != null)
                CreateFloatingWindow(camera);
            else
                LogStatus("No main camera found for RGB preview.", true);

            if (m_StartCaptureOnAwake)
                StartCapture();
        }

        void OnEnable()
        {
            if (m_CameraService == null)
                m_CameraService = RgbCameraFrameService.EnsureInstance(gameObject);
            if (m_CameraService != null)
                m_CameraService.FrameReceived += OnCameraFrame;
        }

        void OnDisable()
        {
            if (m_CameraService != null)
                m_CameraService.FrameReceived -= OnCameraFrame;
            ReleaseLegacyCaptureLease();
        }

        void OnDestroy()
        {
            if (m_CameraService != null)
                m_CameraService.FrameReceived -= OnCameraFrame;
            ReleaseLegacyCaptureLease();

            if (m_FrameMaterial != null)
                Destroy(m_FrameMaterial);
            if (m_PreviewMaterial != null)
                Destroy(m_PreviewMaterial);
        }

        public bool IsWindowVisible => m_WindowVisible;
        public bool HasReceivedFirstFrame => m_ReceivedFirstCameraFrame;
        public bool IsCapturing => m_CameraService != null && m_CameraService.IsCapturing;

        public bool TryGetYuvTextures(out Texture y, out Texture u, out Texture v)
        {
            y = u = v = null;
            if (m_CameraService == null || !m_CameraService.TryGetLatestFrame(out var frame))
                return false;
            y = frame.Y;
            u = frame.U;
            v = frame.V;
            return frame.IsValid;
        }

        public void ToggleWindowVisible()
        {
            SetWindowVisible(!m_WindowVisible);
        }

        public void SetWindowVisible(bool visible)
        {
            m_WindowVisible = visible;
            if (m_WindowRoot != null)
                m_WindowRoot.SetActive(visible);
        }

        /// <summary>
        /// Compatibility entry point for the existing Beam Pro debug UI. It acquires one
        /// shared capture lease and does not affect gesture/streaming leases.
        /// </summary>
        public void StartCapture()
        {
            if (m_LegacyCaptureLease)
                return;
            m_LegacyCaptureLease = true;
            m_CameraService.AcquireCapture(this);
        }

        public void StopCapture()
        {
            ReleaseLegacyCaptureLease();
        }

        public Material CreateYuvMaterialInstance()
        {
            if (m_YuvMaterialTemplate != null)
                return new Material(m_YuvMaterialTemplate);

            var shader = Shader.Find("Unlit/YUVTransRGB");
            return shader != null ? new Material(shader) : null;
        }

        void ReleaseLegacyCaptureLease()
        {
            if (!m_LegacyCaptureLease || m_CameraService == null)
                return;
            m_LegacyCaptureLease = false;
            m_CameraService.ReleaseCapture(this);
        }

        void EnsureGesturePipeline()
        {
            if (!m_EnableOfflineGesturePipeline)
                return;

            var recognizer = FindObjectOfType<RgbHandGestureRecognizer>();
            if (recognizer == null)
                recognizer = gameObject.AddComponent<RgbHandGestureRecognizer>();

            if (FindObjectOfType<RgbSliceGestureController>() == null)
                gameObject.AddComponent<RgbSliceGestureController>();

            recognizer.SetRecognitionEnabled(true);
        }

        void CreateFloatingWindow(Camera camera)
        {
            m_WindowRoot = new GameObject("RGB Camera Window");
            var forward = camera.transform.forward;
            var right = camera.transform.right;
            var up = camera.transform.up;
            m_WindowRoot.transform.SetPositionAndRotation(
                camera.transform.position + forward * m_DistanceMeters + up * m_VerticalOffsetMeters + right * m_HorizontalOffsetMeters,
                Quaternion.LookRotation(forward, up));

            var frameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frameObject.name = "Frame";
            frameObject.transform.SetParent(m_WindowRoot.transform, false);
            frameObject.transform.localScale = new Vector3(m_WidthMeters + 0.02f, m_WidthMeters / m_AspectRatio + 0.02f, 1f);
            Destroy(frameObject.GetComponent<Collider>());
            var frameRenderer = frameObject.GetComponent<MeshRenderer>();
            var standardShader = Shader.Find("Standard");
            if (standardShader != null)
            {
                m_FrameMaterial = new Material(standardShader) { color = new Color(0.08f, 0.08f, 0.08f, 1f) };
                frameRenderer.material = m_FrameMaterial;
            }
            frameRenderer.shadowCastingMode = ShadowCastingMode.Off;
            frameRenderer.receiveShadows = false;

            var previewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            previewObject.name = "RGB Preview";
            previewObject.transform.SetParent(m_WindowRoot.transform, false);
            previewObject.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            previewObject.transform.localScale = new Vector3(m_WidthMeters, m_WidthMeters / m_AspectRatio, 1f);
            Destroy(previewObject.GetComponent<Collider>());
            var previewRenderer = previewObject.GetComponent<MeshRenderer>();
            m_PreviewMaterial = CreateYuvMaterialInstance();
            if (m_PreviewMaterial != null)
                previewRenderer.material = m_PreviewMaterial;
            previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;

            SetWindowVisible(m_WindowVisible);
        }

        void OnCameraFrame(RgbCameraFrame frame)
        {
            if (!frame.IsValid)
                return;

            if (m_PreviewMaterial != null)
            {
                m_PreviewMaterial.SetTexture("_MainTex", frame.Y);
                m_PreviewMaterial.SetTexture("_UTex", frame.U);
                m_PreviewMaterial.SetTexture("_VTex", frame.V);
            }

            if (!m_ReceivedFirstCameraFrame)
            {
                m_ReceivedFirstCameraFrame = true;
                LogStatus("First shared RGB camera frame received.");
            }
        }

        void OnGUI()
        {
            if (!m_ShowDebugOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;
            var state = m_CameraService == null ? "not ready" :
                $"capture={m_CameraService.IsCapturing}, consumers={m_CameraService.ConsumerCount}, plug={m_CameraService.PlugState}";
            BeamProUnifiedLogWindow.SetStatus("RGB 相机", state);
        }

        static void LogStatus(string message, bool warning = false)
        {
            BeamProUnifiedLogWindow.AddLine("RGB 相机", message);
            if (warning)
                Debug.LogWarning($"RGBCameraFloatingWindow: {message}");
            else
                Debug.Log($"RGBCameraFloatingWindow: {message}");
        }
    }
}
