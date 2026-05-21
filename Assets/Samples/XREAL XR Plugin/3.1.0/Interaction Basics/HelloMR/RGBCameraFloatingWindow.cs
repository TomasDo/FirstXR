using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Displays the XREAL Eye RGB camera stream in a world-space floating window.
    /// </summary>
    public class RGBCameraFloatingWindow : MonoBehaviour
    {
        [SerializeField]
        float m_DistanceMeters = 1.5f;

        [SerializeField]
        float m_VerticalOffsetMeters = 0.35f;

        [SerializeField]
        float m_HorizontalOffsetMeters = -0.55f;

        [SerializeField]
        float m_WidthMeters = 0.42f;

        [SerializeField]
        float m_AspectRatio = 16f / 9f;

        [SerializeField]
        bool m_StartCaptureOnAwake = true;

        [SerializeField]
        Material m_YuvMaterialTemplate;

        [SerializeField]
        bool m_SwapUVChannels = false;

        XREALRGBCameraTexture m_RGBCameraTexture;
        RawImage m_PreviewImage;
        Material m_PreviewMaterial;

        void Start()
        {
            StartCoroutine(InitializeWhenCameraReady());
        }

        void Update()
        {
            if (m_RGBCameraTexture == null || m_PreviewImage == null || m_PreviewMaterial == null)
                return;

            var yuvTextures = m_RGBCameraTexture.GetYUVFormatTextures();
            if (yuvTextures == null || yuvTextures.Length < 3)
                return;

            var currentU = m_SwapUVChannels ? yuvTextures[2] : yuvTextures[1];
            var currentV = m_SwapUVChannels ? yuvTextures[1] : yuvTextures[2];

            if (yuvTextures[0] == null || currentU == null || currentV == null)
                return;

            m_PreviewMaterial.SetTexture("_MainTex", yuvTextures[0]);
            m_PreviewMaterial.SetTexture("_UTex", currentU);
            m_PreviewMaterial.SetTexture("_VTex", currentV);

            if (m_PreviewImage.texture != yuvTextures[0])
                m_PreviewImage.texture = yuvTextures[0];
        }

        void OnDestroy()
        {
            StopCapture();

            if (m_PreviewMaterial != null)
                Destroy(m_PreviewMaterial);
        }

        IEnumerator InitializeWhenCameraReady()
        {
            Camera camera = null;
            for (var i = 0; i < 120 && camera == null; i++)
            {
                camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
                if (camera != null)
                    break;

                yield return null;
            }

            if (camera == null)
            {
                Debug.LogWarning("RGBCameraFloatingWindow: No main camera found.");
                yield break;
            }

            CreateFloatingWindow(camera);
            m_RGBCameraTexture = XREALRGBCameraTexture.CreateSingleton();

            if (m_StartCaptureOnAwake)
                StartCapture();
        }

        void CreateFloatingWindow(Camera camera)
        {
            var windowRoot = new GameObject("RGB Camera Window");
            var forward = camera.transform.forward;
            var right = camera.transform.right;
            var up = camera.transform.up;
            var position = camera.transform.position
                + forward * m_DistanceMeters
                + up * m_VerticalOffsetMeters
                + right * m_HorizontalOffsetMeters;

            windowRoot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));

            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(windowRoot.transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            const float pixelScale = 0.001f;
            var widthPixels = m_WidthMeters / pixelScale;
            var heightPixels = m_WidthMeters / m_AspectRatio / pixelScale;

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(widthPixels, heightPixels);
            canvasRect.localScale = Vector3.one * pixelScale;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

            var previewObject = new GameObject("RGB Preview");
            previewObject.transform.SetParent(panelObject.transform, false);

            var previewRect = previewObject.AddComponent<RectTransform>();
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.offsetMin = new Vector2(8f, 8f);
            previewRect.offsetMax = new Vector2(-8f, -8f);

            m_PreviewImage = previewObject.AddComponent<RawImage>();
            m_PreviewImage.color = Color.white;
            m_PreviewImage.raycastTarget = false;

            m_PreviewMaterial = CreatePreviewMaterial();
            if (m_PreviewMaterial != null)
                m_PreviewImage.material = m_PreviewMaterial;

            var canvasRenderer = previewObject.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
                canvasRenderer.cullTransparentMesh = false;
        }

        Material CreatePreviewMaterial()
        {
            if (m_YuvMaterialTemplate != null)
                return new Material(m_YuvMaterialTemplate);

            return CreateYuvMaterial();
        }

        static Material CreateYuvMaterial()
        {
            var shader = Shader.Find("Unlit/YUVTransRGB");
            if (shader == null)
            {
                Debug.LogError("RGBCameraFloatingWindow: Unlit/YUVTransRGB shader not found.");
                return null;
            }

            return new Material(shader);
        }

        public void StartCapture()
        {
            if (m_RGBCameraTexture != null && !m_RGBCameraTexture.IsCapturing)
                m_RGBCameraTexture.StartCapture();
        }

        public void StopCapture()
        {
            if (m_RGBCameraTexture != null && m_RGBCameraTexture.IsCapturing)
                m_RGBCameraTexture.StopCapture();
        }
    }
}
