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
        bool m_ShowBeamProInputToggle = true;

        [SerializeField]
        bool m_DefaultToHandInput = false;

        [SerializeField]
        GameObject m_GlassesControlWindow;

        [SerializeField]
        bool m_GlassesControlWindowVisible = false;

        [SerializeField]
        bool m_ShowBeamProObjectMoveButtons = true;

        [SerializeField]
        bool m_ShowBeamProCheckPlaneAppearanceButtons = true;

        CanvasGroup m_GlassesControlCanvasGroup;
        ReferenceCubeSpawner m_ReferenceCubeSpawner;

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

        void Awake()
        {
            EnsureGlassesControlWindowReference();
            EnsureReferenceCubeSpawnerReference();
        }

        private void Start()
        {
            XREALPlugin.OnTrackingTypeChanged += OnTrackingTypeChanged;
            m_Toggle0Dof.onValueChanged.AddListener(On0DofToggleChanged);
            m_Toggle0DofStable.onValueChanged.AddListener(On0DofStableToggleChanged);
            m_Toggle3Dof.onValueChanged.AddListener(On3DofToggleChanged);
            m_Toggle6Dof.onValueChanged.AddListener(On6DofToggleChanged);

            InitDofUI();
            ApplyDefaultInputOnStart();
            ApplyGlassesControlWindowVisibility();
            RefreshStatusText();
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

        public void SetGlassesControlWindowVisible(bool visible)
        {
            m_GlassesControlWindowVisible = visible;
            ApplyGlassesControlWindowVisibility();
        }

        public void SetTrackingMode(TrackingType trackingType)
        {
            _ = XREALPlugin.SwitchTrackingTypeAsync(trackingType, OnTrackingTypeChanged);
            switch (trackingType)
            {
                case TrackingType.MODE_0DOF:
                    m_Toggle0Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_0DOF_STAB:
                    m_Toggle0DofStable.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_3DOF:
                    m_Toggle3Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_6DOF:
                    m_Toggle6Dof.SetIsOnWithoutNotify(true);
                    break;
            }
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
            m_Toggle0Dof.onValueChanged.RemoveListener(On0DofToggleChanged);
            m_Toggle0DofStable.onValueChanged.RemoveListener(On0DofStableToggleChanged);
            m_Toggle3Dof.onValueChanged.RemoveListener(On3DofToggleChanged);
            m_Toggle6Dof.onValueChanged.RemoveListener(On6DofToggleChanged);
        }

        private void InitDofUI()
        {
            switch (XREALPlugin.GetTrackingType())
            {
                case TrackingType.MODE_0DOF:
                    m_Toggle0Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_0DOF_STAB:
                    m_Toggle0DofStable.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_3DOF:
                    m_Toggle3Dof.SetIsOnWithoutNotify(true);
                    break;
                case TrackingType.MODE_6DOF:
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
            if (!m_ShowBeamProInputToggle || Application.platform != RuntimePlatform.Android)
                return;

            var rowCount = CountRightColumnButtonRows();
            var buttonLayout = BeamProOverlayLayout.ComputeRightColumnButtons(rowCount);
            var x = buttonLayout.X;
            var y = buttonLayout.Y;
            var width = buttonLayout.Width;
            var height = buttonLayout.ButtonHeight;
            var rowSpacing = buttonLayout.RowSpacing;

            bool isHandControl = XREALPlugin.GetInputSource() == InputSource.Hands;
            string inputLabel = isHandControl ? "Switch to Controller" : "Switch to Hand";

            if (GUI.Button(new Rect(x, y, width, height), inputLabel))
            {
                if (isHandControl)
                    ChangeToControllerInput();
                else
                    ChangeToHandInput();
            }

            y += height + rowSpacing;
            string uiLabel = m_GlassesControlWindowVisible ? "Hide Glasses UI" : "Show Glasses UI";
            if (GUI.Button(new Rect(x, y, width, height), uiLabel))
                ToggleGlassesControlWindow();

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
