using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Displays the XREAL Eye RGB camera stream in a world-space floating window.
    /// </summary>
    public class RGBCameraFloatingWindow : MonoBehaviour
    {
        const string AndroidCameraPermission = "android.permission.CAMERA";

        [SerializeField]
        float m_DistanceMeters = 2f;

        [SerializeField]
        float m_VerticalOffsetMeters = 0.35f;

        [SerializeField]
        float m_HorizontalOffsetMeters = -0.55f;

        [SerializeField]
        float m_WidthMeters = 1.26f;

        [SerializeField]
        float m_AspectRatio = 16f / 9f;

        [SerializeField]
        bool m_StartCaptureOnAwake = true;

        [SerializeField]
        Material m_YuvMaterialTemplate;

        [SerializeField]
        bool m_ShowDebugOnBeamPro = true;

        [SerializeField]
        int m_MaxDebugLines = 200;

        [SerializeField]
        float m_WaitForEyePlugTimeoutSeconds = 45f;

        [SerializeField]
        float m_DebugPanelWidth = 920f;

        [SerializeField]
        float m_DebugPanelHeight = 520f;

        [SerializeField]
        bool m_AutoScrollLogsToBottom = true;

        const float k_DebugPanelHeaderHeight = 48f;
        const float k_DebugPanelMargin = 24f;
        const float k_DebugPanelPadding = 12f;

        Vector2 m_DebugPanelPosition = new Vector2(k_DebugPanelMargin, k_DebugPanelMargin);
        Vector2 m_DebugScrollPosition;
        Vector2 m_DebugDragPointerOffset;
        bool m_DebugPanelDragging;
        bool m_ScrollLogsToBottomNextFrame;
        GUIStyle m_DebugLogStyle;
        GUIStyle m_DebugHeaderStyle;

        XREALRGBCameraTexture m_RGBCameraTexture;
        Material m_PreviewMaterial;
        MeshRenderer m_PreviewRenderer;
        bool m_PendingStartCapture;
        bool m_LoggedTextureInfo;
        bool m_SubscribedToCameraUpdates;
        bool m_SubscribedToPlugState;
        XREALRGBCameraPlugState m_RGBCameraPlugState = XREALRGBCameraPlugState.UNKNOWN;
        readonly List<string> m_DebugLines = new List<string>();

        void OnEnable()
        {
            SubscribeToPlugState();
        }

        void OnDisable()
        {
            UnsubscribeFromPlugState();
        }

        void Start()
        {
            StartCoroutine(InitializeWhenCameraReady());
        }

        void OnDestroy()
        {
            UnsubscribeFromCameraUpdates();
            StopCapture();

            if (m_PreviewMaterial != null)
                Destroy(m_PreviewMaterial);
        }

        IEnumerator InitializeWhenCameraReady()
        {
            LogDeviceDiagnostics();

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
                LogStatus("No main camera found.", true);
                yield break;
            }

            CreateFloatingWindow(camera);
            LogStatus("Floating RGB camera window created.");

            yield return RequestCameraPermissionIfNeeded();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAndroidCameraPermissionGranted())
            {
                LogStatus("Android Camera permission was not granted; RGB camera stream cannot start.", true);
                yield break;
            }
#endif

            yield return null;
            m_RGBCameraTexture = XREALRGBCameraTexture.CreateSingleton();
            if (m_RGBCameraTexture == null)
            {
                LogStatus("Failed to create XREAL RGB camera texture singleton.", true);
                yield break;
            }

            LogStatus("XREAL RGB camera texture singleton created.");
            SubscribeToCameraUpdates();

            if (m_StartCaptureOnAwake || m_PendingStartCapture)
                yield return StartCaptureWithRetry();
        }

        IEnumerator StartCaptureWithRetry()
        {
            const int maxAttempts = 15;
            const float retryIntervalSeconds = 2f;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA))
            {
                LogStatus("Device does not report RGB camera support. Connect XREAL Eye to One Pro.", true);
                yield break;
            }

            yield return WaitForRgbCameraPlugIn();
#endif

            yield return new WaitForSeconds(0.5f);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (TryStartCaptureOnce(attempt, maxAttempts))
                    yield break;

                if (attempt < maxAttempts)
                    yield return new WaitForSeconds(retryIntervalSeconds);
            }

            LogStatus("RGB capture failed. Check: XREAL Eye firmly connected, Camera permission granted, and RGBCameraAndCapture sample on this Beam Pro.", true);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        IEnumerator WaitForRgbCameraPlugIn()
        {
            if (m_RGBCameraPlugState == XREALRGBCameraPlugState.PLUGIN)
            {
                LogStatus("XREAL Eye RGB camera already plugged in.");
                yield break;
            }

            LogStatus($"Waiting for XREAL Eye PLUGIN state (current: {m_RGBCameraPlugState})...");
            var deadline = Time.realtimeSinceStartup + m_WaitForEyePlugTimeoutSeconds;
            while (m_RGBCameraPlugState != XREALRGBCameraPlugState.PLUGIN
                && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(0.5f);
            }

            if (m_RGBCameraPlugState == XREALRGBCameraPlugState.PLUGIN)
            {
                LogStatus("XREAL Eye RGB camera PLUGIN detected.");
                yield break;
            }

            LogStatus($"Timed out waiting for PLUGIN (last state: {m_RGBCameraPlugState}). Will still try native StartCapture.", true);
        }
#endif

        IEnumerator RequestCameraPermissionIfNeeded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (IsAndroidCameraPermissionGranted())
                yield break;

            LogStatus("Requesting Camera permission via XREAL Android permissions.");

            var permissionTask = XREALAndroidPermissionsManager.RequestPermission(AndroidCameraPermission);
            if (permissionTask == null)
            {
                LogStatus("Another Android permission request is already in progress.", true);
                yield break;
            }

            yield return permissionTask.WaitForCompletion();

            if (permissionTask.Result.IsAllGranted)
                LogStatus("Android Camera permission granted.");
            else
                LogStatus($"Android Camera permission denied. {permissionTask.Result}", true);
#else
            yield break;
#endif
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

            var frameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frameObject.name = "Frame";
            frameObject.transform.SetParent(windowRoot.transform, false);
            frameObject.transform.localPosition = Vector3.zero;
            frameObject.transform.localRotation = Quaternion.identity;
            frameObject.transform.localScale = new Vector3(m_WidthMeters + 0.02f, m_WidthMeters / m_AspectRatio + 0.02f, 1f);

            var frameCollider = frameObject.GetComponent<Collider>();
            if (frameCollider != null)
                Destroy(frameCollider);

            var frameRenderer = frameObject.GetComponent<MeshRenderer>();
            var frameMaterial = new Material(Shader.Find("Standard"));
            frameMaterial.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            frameRenderer.material = frameMaterial;
            frameRenderer.shadowCastingMode = ShadowCastingMode.Off;
            frameRenderer.receiveShadows = false;

            var previewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            previewObject.name = "RGB Preview";
            previewObject.transform.SetParent(windowRoot.transform, false);
            previewObject.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            previewObject.transform.localRotation = Quaternion.identity;
            previewObject.transform.localScale = new Vector3(m_WidthMeters, m_WidthMeters / m_AspectRatio, 1f);

            var previewCollider = previewObject.GetComponent<Collider>();
            if (previewCollider != null)
                Destroy(previewCollider);

            m_PreviewRenderer = previewObject.GetComponent<MeshRenderer>();
            var previewMaterial = CreatePreviewMaterial();
            if (previewMaterial != null)
                m_PreviewRenderer.material = previewMaterial;

            m_PreviewMaterial = m_PreviewRenderer.material;

            m_PreviewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_PreviewRenderer.receiveShadows = false;
        }

        Material CreatePreviewMaterial()
        {
            if (m_YuvMaterialTemplate != null)
                return new Material(m_YuvMaterialTemplate);

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
            if (m_RGBCameraTexture == null)
            {
                LogStatus("StartCapture requested before camera texture was ready.");
                m_PendingStartCapture = true;
                return;
            }

            m_PendingStartCapture = false;
            StartCoroutine(StartCaptureWithRetry());
        }

        bool TryStartCaptureOnce(int attempt, int maxAttempts)
        {
            if (m_RGBCameraTexture.IsCapturing)
            {
                LogStatus($"RGB camera already capturing (attempt {attempt}/{maxAttempts}).");
                return true;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA))
            {
                LogStatus("RGB camera feature unsupported on current HMD.", true);
                return false;
            }

            if (m_RGBCameraPlugState == XREALRGBCameraPlugState.PLUGOUT)
            {
                LogStatus("XREAL Eye is unplugged (PLUGOUT). Reconnect Eye and retry.", true);
                return false;
            }
#endif

            LogStatus($"Starting RGB camera capture (attempt {attempt}/{maxAttempts}, plug={m_RGBCameraPlugState}).");
            var started = m_RGBCameraTexture.StartCapture();
            if (started)
            {
                LogStatus("RGB camera capture started successfully.");
                return true;
            }

            LogStatus("Native StartRGBCameraDataCapture failed. Eye may be disconnected, busy, or not ready.", true);
            return false;
        }

        void SubscribeToPlugState()
        {
            if (m_SubscribedToPlugState)
                return;

            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState += OnRgbCameraPlugStateChanged;
            m_SubscribedToPlugState = true;
        }

        void UnsubscribeFromPlugState()
        {
            if (!m_SubscribedToPlugState)
                return;

            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState -= OnRgbCameraPlugStateChanged;
            m_SubscribedToPlugState = false;
        }

        void OnRgbCameraPlugStateChanged(XREALRGBCameraPlugState state)
        {
            m_RGBCameraPlugState = state;
            LogStatus($"RGB camera plug state: {state}");

            if (state == XREALRGBCameraPlugState.PLUGOUT)
                StopCapture();
            else if (state == XREALRGBCameraPlugState.PLUGIN && m_RGBCameraTexture != null
                && !m_RGBCameraTexture.IsCapturing && (m_StartCaptureOnAwake || m_PendingStartCapture))
                StartCoroutine(StartCaptureWithRetry());
        }

        void SubscribeToCameraUpdates()
        {
            if (m_RGBCameraTexture == null || m_SubscribedToCameraUpdates)
                return;

            m_RGBCameraTexture.OnRGBCameraUpdate += OnRGBCameraFrameUpdated;
            m_SubscribedToCameraUpdates = true;
        }

        void UnsubscribeFromCameraUpdates()
        {
            if (m_RGBCameraTexture == null || !m_SubscribedToCameraUpdates)
                return;

            m_RGBCameraTexture.OnRGBCameraUpdate -= OnRGBCameraFrameUpdated;
            m_SubscribedToCameraUpdates = false;
        }

        void OnRGBCameraFrameUpdated()
        {
            if (m_RGBCameraTexture == null || m_PreviewMaterial == null)
                return;

            var yuvTextures = m_RGBCameraTexture.GetYUVFormatTextures();
            if (yuvTextures == null || yuvTextures.Length < 3
                || yuvTextures[0] == null || yuvTextures[1] == null || yuvTextures[2] == null)
                return;

            if (!m_LoggedTextureInfo)
            {
                LogStatus($"RGB camera textures ready. Y={yuvTextures[0].width}x{yuvTextures[0].height}, U={yuvTextures[1].width}x{yuvTextures[1].height}, V={yuvTextures[2].width}x{yuvTextures[2].height}");
                m_LoggedTextureInfo = true;
            }

            m_PreviewMaterial.mainTexture = yuvTextures[0];
            m_PreviewMaterial.SetTexture("_MainTex", yuvTextures[0]);
            m_PreviewMaterial.SetTexture("_UTex", yuvTextures[1]);
            m_PreviewMaterial.SetTexture("_VTex", yuvTextures[2]);
        }

        public void StopCapture()
        {
            if (m_RGBCameraTexture != null && m_RGBCameraTexture.IsCapturing)
                m_RGBCameraTexture.StopCapture();
        }

        void LogDeviceDiagnostics()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var deviceType = XREALPlugin.GetDeviceType();
            var deviceCategory = XREALPlugin.GetDeviceCategory();
            var rgbSupported = XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA);
            LogStatus($"Device type={deviceType}, category={deviceCategory}, RGB feature={rgbSupported}, plug={m_RGBCameraPlugState}");

            if (rgbSupported)
            {
                var size = Vector2Int.zero;
                if (XREALPlugin.GetDeviceResolution(XREALComponent.XREAL_COMPONENT_RGB_CAMERA, ref size) && size.x > 0)
                    LogStatus($"RGB camera resolution from SDK: {size.x}x{size.y}");
            }
#endif
        }

        static bool IsAndroidCameraPermissionGranted()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return XREALAndroidPermissionsManager.IsPermissionGranted(AndroidCameraPermission);
#else
            return true;
#endif
        }

        void LogStatus(string message, bool warning = false)
        {
            var line = $"[{Time.realtimeSinceStartup:F1}s] {message}";
            m_DebugLines.Add(line);
            while (m_DebugLines.Count > Mathf.Max(1, m_MaxDebugLines))
                m_DebugLines.RemoveAt(0);

            m_ScrollLogsToBottomNextFrame = m_AutoScrollLogsToBottom;

            var unityLog = $"RGBCameraFloatingWindow: {message}";
            if (warning)
                Debug.LogWarning(unityLog);
            else
                Debug.Log(unityLog);
        }

        void OnGUI()
        {
            if (!m_ShowDebugOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            EnsureDebugGuiStyles();
            var panelRect = GetDebugPanelRect();
            HandleDebugPanelDrag(panelRect);

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = previousColor;

            var headerRect = new Rect(panelRect.x, panelRect.y, panelRect.width, k_DebugPanelHeaderHeight);
            GUI.Label(headerRect, "  RGB Debug Log (drag header)", m_DebugHeaderStyle);

            var text = BuildDebugText();
            var scrollViewRect = new Rect(
                panelRect.x + k_DebugPanelPadding,
                panelRect.y + k_DebugPanelHeaderHeight,
                panelRect.width - k_DebugPanelPadding * 2f,
                panelRect.height - k_DebugPanelHeaderHeight - k_DebugPanelPadding);

            var innerWidth = scrollViewRect.width - 24f;
            var contentHeight = m_DebugLogStyle.CalcHeight(new GUIContent(text), innerWidth);
            contentHeight = Mathf.Max(contentHeight, scrollViewRect.height);
            var contentRect = new Rect(0f, 0f, innerWidth, contentHeight);

            if (m_ScrollLogsToBottomNextFrame)
            {
                m_DebugScrollPosition.y = Mathf.Max(0f, contentHeight - scrollViewRect.height);
                m_ScrollLogsToBottomNextFrame = false;
            }

            m_DebugScrollPosition = GUI.BeginScrollView(scrollViewRect, m_DebugScrollPosition, contentRect);
            GUI.Label(new Rect(0f, 0f, innerWidth, contentHeight), text, m_DebugLogStyle);
            GUI.EndScrollView();
        }

        void EnsureDebugGuiStyles()
        {
            var fontSize = Mathf.Max(14, Screen.height / 64);
            if (m_DebugLogStyle != null && m_DebugLogStyle.fontSize == fontSize)
                return;

            m_DebugLogStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = false,
                clipping = TextClipping.Overflow
            };

            m_DebugHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.75f, 0.9f, 1f) }
            };
        }

        Rect GetDebugPanelRect()
        {
            var rect = BeamProOverlayLayout.ClampRgbDebugPanelRect(
                m_DebugPanelWidth,
                m_DebugPanelHeight,
                m_DebugPanelPosition);
            m_DebugPanelPosition = rect.position;
            return rect;
        }

        void HandleDebugPanelDrag(Rect panelRect)
        {
            var headerRect = new Rect(panelRect.x, panelRect.y, panelRect.width, k_DebugPanelHeaderHeight);
            var e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!headerRect.Contains(e.mousePosition))
                        break;
                    m_DebugPanelDragging = true;
                    m_DebugDragPointerOffset = e.mousePosition - m_DebugPanelPosition;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!m_DebugPanelDragging)
                        break;
                    m_DebugPanelPosition = e.mousePosition - m_DebugDragPointerOffset;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    m_DebugPanelDragging = false;
                    break;
            }
        }

        string BuildDebugText()
        {
            var captureState = m_RGBCameraTexture == null
                ? "not created"
                : m_RGBCameraTexture.IsCapturing ? "capturing" : "stopped";

#if UNITY_ANDROID && !UNITY_EDITOR
            var permissionState = IsAndroidCameraPermissionGranted() ? "granted" : "not granted";
            var rgbFeature = XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA);
            var deviceType = XREALPlugin.GetDeviceType();
            var header = $"RGB Debug | Capture: {captureState} | Perm: {permissionState}\nDevice: {deviceType} | RGB feature: {rgbFeature} | Eye plug: {m_RGBCameraPlugState}";
#else
            var header = $"RGB Camera Debug | Capture: {captureState}";
#endif

            var logs = m_DebugLines.Count > 0 ? string.Join("\n", m_DebugLines) : "No RGB camera logs yet.";
            return $"{header}\n{logs}";
        }
    }
}
