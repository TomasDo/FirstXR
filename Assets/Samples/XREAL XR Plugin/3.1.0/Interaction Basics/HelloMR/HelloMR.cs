using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// This sample demonstrates how to switch between different tracking modes and input sources.
    /// </summary>
    public class HelloMR : MonoBehaviour
    {
        [SerializeField]
        bool m_EngineerMode = false;

        [SerializeField]
        bool m_HudVisibleOnStart = true;

        [SerializeField]
        bool m_NavWidgetVisibleOnStart = false;

        [SerializeField]
        bool m_ShowBeamProInputToggle = false;

        [SerializeField]
        bool m_DefaultToHandInput = false;

        [SerializeField]
        GameObject m_GlassesControlWindow;

        [SerializeField]
        bool m_GlassesControlWindowVisible = false;

        [SerializeField]
        bool m_ShowBeamProObjectMoveButtons = false;

        [SerializeField]
        bool m_ShowBeamProCheckPlaneAppearanceButtons = false;

        [SerializeField]
        bool m_ShowDentalRobotBeamProPanel = true;

        [SerializeField]
        string m_DentalRobotServerHost = DentalRobotConnectionDefaults.ServerHost;

        [SerializeField]
        int m_DentalRobotServerPort = DentalRobotConnectionDefaults.ServerPort;

        [SerializeField]
        string m_DentalRobotDeviceId = DentalRobotConnectionDefaults.DeviceId;

        [SerializeField]
        string m_DentalRobotDatasetId = DentalRobotConnectionDefaults.DatasetId;

        [SerializeField]
        bool m_ShowBeamProGestureToggle = false;

        [SerializeField]
        bool m_EnableRgbGestureRecognition = true;

        CanvasGroup m_GlassesControlCanvasGroup;
        ReferenceCubeSpawner m_ReferenceCubeSpawner;
        RGBCameraFloatingWindow m_RGBCameraFloatingWindow;
        RgbHandGestureRecognizer m_RgbHandGestureRecognizer;

        [SerializeField]
        TMP_Text m_TextCurrentMode;
        [SerializeField]
        Toggle m_Toggle0Dof;
        [SerializeField]
        Toggle m_Toggle0DofStable;
        [SerializeField]
        Toggle m_Toggle3Dof;
        [SerializeField]
        Toggle m_Toggle6Dof;
        [SerializeField]
        Button m_ButtonHandInput;

        const string k_SwitchToControllerLabel = "Switch to Controller";
        const string k_SwitchToHandLabel = "Switch to Hand";
        const string k_ShowGlassesUiLabel = "Show Glasses UI";
        const string k_HideGlassesUiLabel = "Hide Glasses UI";
        const string k_ShowRgbWindowLabel = "Show RGB Window";
        const string k_HideRgbWindowLabel = "Hide RGB Window";
        const string k_ShowHudLabel = "Show HUD";
        const string k_HideHudLabel = "Hide HUD";
        const string k_ShowWidgetLabel = "Show Nav Widget";
        const string k_HideWidgetLabel = "Hide Nav Widget";
        const string k_EnableGestureLabel = "Enable Gesture";
        const string k_DisableGestureLabel = "Disable Gesture";

        void Awake()
        {
            BeamProUnifiedLogWindow.EnsureInstance();
            BeamProUnifiedLogWindow.SetVisible(m_EngineerMode);
            EnsureGlassesControlWindowReference();
            EnsureReferenceCubeSpawnerReference();
            EnsureRGBCameraFloatingWindowReference();
            EnsureRgbHandGestureRecognizer();
            if (m_RgbHandGestureRecognizer != null)
                m_RgbHandGestureRecognizer.SetRecognitionEnabled(m_EnableRgbGestureRecognition);

            EnsureDentalRobotBeamProPanel();
            ApplyProductSurfaceDefaults();
        }

        private void Start()
        {
            XREALPlugin.OnTrackingTypeChanged += OnTrackingTypeChanged;
            if (m_Toggle0Dof != null)
                m_Toggle0Dof.onValueChanged.AddListener(On0DofToggleChanged);
            if (m_Toggle0DofStable != null)
                m_Toggle0DofStable.onValueChanged.AddListener(On0DofStableToggleChanged);
            if (m_Toggle3Dof != null)
                m_Toggle3Dof.onValueChanged.AddListener(On3DofToggleChanged);
            if (m_Toggle6Dof != null)
                m_Toggle6Dof.onValueChanged.AddListener(On6DofToggleChanged);

            InitDofUI();
            ApplyDefaultInputOnStart();
            ApplyGlassesControlWindowVisibility();
            RefreshStatusText();
            if (m_ButtonHandInput != null)
                m_ButtonHandInput.interactable = XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_PERCEPTION_HEAD_TRACKING_POSITION);
        }

        void EnsureGlassesControlWindowReference()
        {
            if (m_GlassesControlWindow != null)
                return;

            var canvas = GameObject.Find("Canvas");
            if (canvas != null)
                m_GlassesControlWindow = canvas;
        }

        void EnsureReferenceCubeSpawnerReference()
        {
            if (m_ReferenceCubeSpawner == null)
                m_ReferenceCubeSpawner = FindObjectOfType<ReferenceCubeSpawner>();
        }

        void EnsureRGBCameraFloatingWindowReference()
        {
            if (m_RGBCameraFloatingWindow == null)
                m_RGBCameraFloatingWindow = FindObjectOfType<RGBCameraFloatingWindow>();
        }

        void EnsureRgbHandGestureRecognizer()
        {
            if (m_RgbHandGestureRecognizer == null)
                m_RgbHandGestureRecognizer = FindObjectOfType<RgbHandGestureRecognizer>();

            if (m_RgbHandGestureRecognizer == null)
            {
                var go = new GameObject("RGB Hand Gesture Recognizer");
                m_RgbHandGestureRecognizer = go.AddComponent<RgbHandGestureRecognizer>();
            }
        }

        void EnsureDentalRobotBeamProPanel()
        {
            if (!m_ShowDentalRobotBeamProPanel)
                return;

            DentalNavigationState.EnsureInstance();
            var layout = DentalDisplayLayoutController.EnsureInstance();
            DentalCtVolumeService.EnsureInstance();
            DentalHudController.EnsureInstance();
            DentalHudController.Instance.SetHudVisible(layout != null ? layout.HudVisible : m_HudVisibleOnStart);
            DentalHudController.Instance.SetWidgetFrameVisible(layout != null ? layout.ModelVisible : m_NavWidgetVisibleOnStart);

            if (FindObjectOfType<DentalRobotModelRenderer>() == null)
            {
                var renderer = new GameObject("Dental Robot Model Renderer");
                renderer.AddComponent<DentalRobotModelRenderer>();
            }

            if (DentalRobotModelRenderer.Instance != null)
                DentalRobotModelRenderer.Instance.SetWidgetVisible(layout != null ? layout.ModelVisible : m_NavWidgetVisibleOnStart);

            var existingClient = FindObjectOfType<DentalRobotGrpcClient>();
            if (existingClient != null)
            {
                existingClient.ConfigureEndpoint(
                    m_DentalRobotServerHost,
                    m_DentalRobotServerPort,
                    m_DentalRobotDeviceId,
                    m_DentalRobotDatasetId);
            }
            else
            {
                var client = new GameObject("Dental Robot gRPC Client");
                client.AddComponent<DentalRobotGrpcClient>().ConfigureEndpoint(
                    m_DentalRobotServerHost,
                    m_DentalRobotServerPort,
                    m_DentalRobotDeviceId,
                    m_DentalRobotDatasetId);
            }

            var existingDisplay = FindObjectOfType<DentalRobotBeamProDisplay>();
            if (existingDisplay != null)
            {
                existingDisplay.ConfigureEndpoint(
                    m_DentalRobotServerHost,
                    m_DentalRobotServerPort,
                    m_DentalRobotDeviceId,
                    m_DentalRobotDatasetId);
                return;
            }

            var panel = new GameObject("Dental Robot Beam Pro Display");
            panel.AddComponent<DentalRobotBeamProDisplay>().ConfigureEndpoint(
                m_DentalRobotServerHost,
                m_DentalRobotServerPort,
                m_DentalRobotDeviceId,
                m_DentalRobotDatasetId);
        }

        void ApplyProductSurfaceDefaults()
        {
            if (m_EngineerMode)
                return;

            EnsureRGBCameraFloatingWindowReference();
            if (m_RGBCameraFloatingWindow != null)
                m_RGBCameraFloatingWindow.SetWindowVisible(false);
        }

        void ApplyGlassesControlWindowVisibility()
        {
            if (m_GlassesControlWindow == null)
                return;

            if (m_GlassesControlCanvasGroup == null)
                m_GlassesControlCanvasGroup = m_GlassesControlWindow.GetComponent<CanvasGroup>();

            if (m_GlassesControlCanvasGroup == null)
                m_GlassesControlCanvasGroup = m_GlassesControlWindow.AddComponent<CanvasGroup>();

            m_GlassesControlCanvasGroup.alpha = m_GlassesControlWindowVisible ? 1f : 0f;
            m_GlassesControlCanvasGroup.interactable = m_GlassesControlWindowVisible;
            m_GlassesControlCanvasGroup.blocksRaycasts = m_GlassesControlWindowVisible;
        }

        public void ToggleGlassesControlWindow()
        {
            m_GlassesControlWindowVisible = !m_GlassesControlWindowVisible;
            ApplyGlassesControlWindowVisibility();
        }

        void ApplyDefaultInputOnStart()
        {
            if (m_DefaultToHandInput)
                ChangeToHandInput();
            else
                ChangeToControllerInput();
        }

        void RefreshStatusText()
        {
            if (m_TextCurrentMode == null)
                return;

            m_TextCurrentMode.text = $"Track: {XREALPlugin.GetTrackingType()}, Input: {XREALPlugin.GetInputSource()}";
        }

        private void OnDestroy()
        {
            XREALPlugin.OnTrackingTypeChanged -= OnTrackingTypeChanged;
            if (m_Toggle0Dof != null)
                m_Toggle0Dof.onValueChanged.RemoveListener(On0DofToggleChanged);
            if (m_Toggle0DofStable != null)
                m_Toggle0DofStable.onValueChanged.RemoveListener(On0DofStableToggleChanged);
            if (m_Toggle3Dof != null)
                m_Toggle3Dof.onValueChanged.RemoveListener(On3DofToggleChanged);
            if (m_Toggle6Dof != null)
                m_Toggle6Dof.onValueChanged.RemoveListener(On6DofToggleChanged);
        }

        private void InitDofUI()
        {
            switch (XREALPlugin.GetTrackingType())
            {
                case TrackingType.MODE_0DOF:
                    if (m_Toggle0Dof != null)
                        m_Toggle0Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_0DOF_STAB:
                    if (m_Toggle0DofStable != null)
                        m_Toggle0DofStable.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_3DOF:
                    if (m_Toggle3Dof != null)
                        m_Toggle3Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_6DOF:
                    if (m_Toggle6Dof != null)
                        m_Toggle6Dof.SetIsOnWithoutNotify(true);
                    break;
            }
        }

        private void On6DofToggleChanged(bool on)
        {
            if (on)
            {
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_6DOF, OnTrackingTypeChanged);
            }
        }

        private void On3DofToggleChanged(bool on)
        {
            if (on)
            {
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_3DOF, OnTrackingTypeChanged);
            }
        }

        private void On0DofStableToggleChanged(bool on)
        {
            if (on)
            {
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_0DOF_STAB, OnTrackingTypeChanged);
            }
        }

        private void On0DofToggleChanged(bool on)
        {
            if (on)
            {
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_0DOF, OnTrackingTypeChanged);
            }
        }

        /// <summary>
        /// Changes the input source to controller.
        /// </summary>
        public void ChangeToControllerInput()
        {
            XREALPlugin.SetInputSource(InputSource.Controller);
            XREALInput.SetInputSource(InputSource.Controller);
            RefreshStatusText();
        }

        /// <summary>
        /// Changes the input source to hand.
        /// </summary>
        public void ChangeToHandInput()
        {
            XREALPlugin.SetInputSource(InputSource.Hands);
            XREALInput.SetInputSource(InputSource.Hands);
            RefreshStatusText();
        }

        void OnGUI()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            var rowCount = CountRightColumnButtonRows();
            var buttonLayout = BeamProOverlayLayout.ComputeRightColumnButtons(rowCount);
            var x = buttonLayout.X;
            var y = buttonLayout.Y;
            var width = buttonLayout.Width;
            var height = buttonLayout.ButtonHeight;
            var rowSpacing = buttonLayout.RowSpacing;

            var hud = DentalHudController.Instance;
            var layout = DentalDisplayLayoutController.EnsureInstance();
            var hudVisible = layout != null ? layout.HudVisible : hud == null || hud.HudVisible;
            if (GUI.Button(new Rect(x, y, width, height), hudVisible ? k_HideHudLabel : k_ShowHudLabel))
            {
                if (layout != null)
                    layout.SetHudVisibleLocally(!hudVisible);
                DentalHudController.EnsureInstance().SetHudVisible(!hudVisible);
            }

            y += height + rowSpacing;
            var widgetVisible = layout != null
                ? layout.ModelVisible
                : DentalRobotModelRenderer.Instance == null || DentalRobotModelRenderer.Instance.WidgetVisible;
            if (GUI.Button(new Rect(x, y, width, height), widgetVisible ? k_HideWidgetLabel : k_ShowWidgetLabel))
            {
                var next = !widgetVisible;
                if (layout != null)
                    layout.SetModelVisibleLocally(next);
                if (DentalRobotModelRenderer.Instance != null)
                    DentalRobotModelRenderer.Instance.SetWidgetVisible(next);
                if (DentalHudController.Instance != null)
                    DentalHudController.Instance.SetWidgetFrameVisible(next);
            }

            y += height + rowSpacing;
            if (!m_EngineerMode)
                return;

            bool isHandControl = XREALPlugin.GetInputSource() == InputSource.Hands;
            string inputLabel = isHandControl ? k_SwitchToControllerLabel : k_SwitchToHandLabel;

            if (m_ShowBeamProInputToggle && GUI.Button(new Rect(x, y, width, height), inputLabel))
            {
                if (isHandControl)
                    ChangeToControllerInput();
                else
                    ChangeToHandInput();
            }

            if (m_ShowBeamProInputToggle)
                y += height + rowSpacing;

            string uiLabel = m_GlassesControlWindowVisible ? k_HideGlassesUiLabel : k_ShowGlassesUiLabel;
            if (GUI.Button(new Rect(x, y, width, height), uiLabel))
                ToggleGlassesControlWindow();

            EnsureRGBCameraFloatingWindowReference();
            if (m_RGBCameraFloatingWindow != null)
            {
                y += height + rowSpacing;
                string rgbLabel = m_RGBCameraFloatingWindow.IsWindowVisible ? k_HideRgbWindowLabel : k_ShowRgbWindowLabel;
                if (GUI.Button(new Rect(x, y, width, height), rgbLabel))
                    m_RGBCameraFloatingWindow.ToggleWindowVisible();
            }

            if (m_ShowBeamProGestureToggle)
            {
                EnsureRgbHandGestureRecognizer();
                y += height + rowSpacing;
                var gestureOn = m_RgbHandGestureRecognizer != null && m_RgbHandGestureRecognizer.RecognitionEnabled;
                var gestureLabel = gestureOn ? k_DisableGestureLabel : k_EnableGestureLabel;
                if (GUI.Button(new Rect(x, y, width, height), gestureLabel))
                {
                    if (m_RgbHandGestureRecognizer != null)
                    {
                        m_RgbHandGestureRecognizer.ToggleRecognitionEnabled();
                        m_EnableRgbGestureRecognition = m_RgbHandGestureRecognizer.RecognitionEnabled;
                    }
                }
            }

            if (!m_ShowBeamProObjectMoveButtons)
                return;

            y += height + rowSpacing;
            y = DrawMoveButtons(x, y, width, height, rowSpacing);

            if (!m_ShowBeamProCheckPlaneAppearanceButtons)
                return;

            DrawCheckPlaneAppearanceButtons(x, y, width, height, rowSpacing);
        }

        int CountRightColumnButtonRows()
        {
            var rows = 2;
            if (!m_EngineerMode)
                return rows;

            if (m_ShowBeamProInputToggle)
                rows += 1;

            rows += 1;
            EnsureRGBCameraFloatingWindowReference();
            if (m_RGBCameraFloatingWindow != null)
                rows += 1;

            if (m_ShowBeamProGestureToggle)
                rows += 1;

            if (m_ShowBeamProObjectMoveButtons)
                rows += 3;

            if (m_ShowBeamProCheckPlaneAppearanceButtons)
            {
                EnsureReferenceCubeSpawnerReference();
                if (m_ReferenceCubeSpawner != null && m_ReferenceCubeSpawner.HasCheckPlane)
                    rows += 4;
            }

            return rows;
        }

        float DrawMoveButtons(float x, float y, float width, float height, float rowSpacing)
        {
            EnsureReferenceCubeSpawnerReference();
            if (m_ReferenceCubeSpawner == null)
                return y;

            var buttonWidth = (width - 12f) * 0.5f;

            if (GUI.Button(new Rect(x, y, buttonWidth, height), "Move X+"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.right);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "Move X-"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.left);

            y += height + rowSpacing;
            if (GUI.Button(new Rect(x, y, buttonWidth, height), "Move Y+"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.up);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "Move Y-"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.down);

            y += height + rowSpacing;
            if (GUI.Button(new Rect(x, y, buttonWidth, height), "Move Z+"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.forward);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "Move Z-"))
                m_ReferenceCubeSpawner.MoveTargetsByDirection(Vector3.back);

            return y + height + rowSpacing;
        }

        void DrawCheckPlaneAppearanceButtons(float x, float y, float width, float height, float rowSpacing)
        {
            EnsureReferenceCubeSpawnerReference();
            if (m_ReferenceCubeSpawner == null || !m_ReferenceCubeSpawner.HasCheckPlane)
                return;

            var buttonWidth = (width - 12f) * 0.5f;
            const int colorStep = 25;

            if (GUI.Button(new Rect(x, y, buttonWidth, height), "Trans +10%"))
                m_ReferenceCubeSpawner.IncreaseCheckPlaneTransparency();
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "Trans -10%"))
                m_ReferenceCubeSpawner.DecreaseCheckPlaneTransparency();

            y += height + rowSpacing;
            if (GUI.Button(new Rect(x, y, buttonWidth, height), "R+"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(0, colorStep);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "R-"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(0, -colorStep);

            y += height + rowSpacing;
            if (GUI.Button(new Rect(x, y, buttonWidth, height), "G+"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(1, colorStep);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "G-"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(1, -colorStep);

            y += height + rowSpacing;
            if (GUI.Button(new Rect(x, y, buttonWidth, height), "B+"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(2, colorStep);
            if (GUI.Button(new Rect(x + buttonWidth + 12f, y, buttonWidth, height), "B-"))
                m_ReferenceCubeSpawner.AdjustCheckPlaneColorChannel(2, -colorStep);
        }

        /// <summary>
        /// Vibrates the controller.
        /// </summary>
        public void Vibrate()
        {
            if (XREALVirtualController.Singleton != null)
                XREALVirtualController.Singleton.Controller.SendHapticImpulse(0, 0.25f, 0.1f);
        }

        private void OnTrackingTypeChanged(bool result, TrackingType targetTrackingType)
        {
            RefreshStatusText();
        }
    }
}
