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
        bool m_EnableHandTrackingVisualizationOnStart = true;

        [SerializeField]
        GameObject[] m_HandVisualizers;

        static readonly string[] s_HandInteractorObjectNames =
        {
            "Poke Interactor",
            "Direct Interactor",
            "Near-Far Interactor",
            "Ray Interactor",
        };

        [SerializeField]
        GameObject m_GlassesControlWindow;

        [SerializeField]
        bool m_GlassesControlWindowVisible = false;

        [SerializeField]
        bool m_ShowBeamProObjectMoveButtons = true;

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
            EnsureHandVisualizerReferences();
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

        void EnsureHandVisualizerReferences()
        {
            if (m_HandVisualizers != null && m_HandVisualizers.Length > 0)
                return;

            var left = GameObject.Find("Left Hand Tracking");
            var right = GameObject.Find("Right Hand Tracking");
            if (left != null && right != null)
                m_HandVisualizers = new[] { left, right };
            else
                Debug.LogWarning($"[HelloMR] Hand visualizers not found (left={left != null}, right={right != null}).");
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
            else if (m_EnableHandTrackingVisualizationOnStart)
                ApplyControllerWithHandVisualizationOnly();
            else
                ChangeToControllerInput();
        }

        void RefreshStatusText()
        {
            if (m_TextCurrentMode == null)
                return;

            var inputLabel = FormatInputSourceLabel(XREALPlugin.GetInputSource());
            m_TextCurrentMode.text = $"Track: {XREALPlugin.GetTrackingType()}, Input: {inputLabel}";
        }

        static string FormatInputSourceLabel(InputSource source)
        {
            if (source == InputSource.ControllerAndHands)
                return "Controller (hand tracking display only)";

            return source.ToString();
        }

        static bool IsHandControlInput(InputSource source)
        {
            return source == InputSource.Hands;
        }

        void SetHandVisualizersActive(bool active)
        {
            EnsureHandVisualizerReferences();
            if (m_HandVisualizers == null)
                return;

            foreach (var handRoot in m_HandVisualizers)
            {
                if (handRoot != null)
                    handRoot.SetActive(active);
            }
        }

        void SetHandInteractorsEnabled(bool enabled)
        {
            foreach (var handObjectName in new[] { "Left Hand", "Right Hand" })
            {
                var handRoot = GameObject.Find(handObjectName);
                if (handRoot == null)
                    continue;

                foreach (var interactorTransform in handRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (System.Array.IndexOf(s_HandInteractorObjectNames, interactorTransform.name) < 0)
                        continue;

                    interactorTransform.gameObject.SetActive(enabled);
                }
            }
        }

        void SyncPluginInputSource(InputSource source)
        {
            XREALPlugin.SetInputSource(source);
            XREALInput.SetInputSource(source);
        }

        /// <summary>
        /// Controller drives interaction; hand tracking runs for visualization only.
        /// </summary>
        public void ApplyControllerWithHandVisualizationOnly()
        {
            SyncPluginInputSource(InputSource.ControllerAndHands);
            SetHandVisualizersActive(true);
            SetHandInteractorsEnabled(false);
            RefreshStatusText();
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
            if (m_EnableHandTrackingVisualizationOnStart)
                ApplyControllerWithHandVisualizationOnly();
            else
            {
                SyncPluginInputSource(InputSource.Controller);
                SetHandVisualizersActive(false);
                SetHandInteractorsEnabled(false);
                RefreshStatusText();
            }
        }

        /// <summary>
        /// Changes the input source to hand.
        /// </summary>
        public void ChangeToHandInput()
        {
            SyncPluginInputSource(InputSource.Hands);
            SetHandVisualizersActive(true);
            SetHandInteractorsEnabled(true);
            RefreshStatusText();
        }

        void OnGUI()
        {
            if (!m_ShowBeamProInputToggle || Application.platform != RuntimePlatform.Android)
                return;

            const float width = 280f;
            const float height = 90f;
            const float margin = 30f;
            const float spacing = 12f;
            var x = Screen.width - width - margin;
            var y = margin;

            var currentSource = XREALPlugin.GetInputSource();
            bool isHandControl = IsHandControlInput(currentSource);
            string inputLabel = isHandControl ? "Switch to Controller" : "Switch to Hand";

            if (GUI.Button(new Rect(x, y, width, height), inputLabel))
            {
                if (isHandControl)
                    ChangeToControllerInput();
                else
                    ChangeToHandInput();
            }

            y += height + spacing;
            string uiLabel = m_GlassesControlWindowVisible ? "Hide Glasses UI" : "Show Glasses UI";
            if (GUI.Button(new Rect(x, y, width, height), uiLabel))
                ToggleGlassesControlWindow();

            if (!m_ShowBeamProObjectMoveButtons)
                return;

            y += height + spacing;
            DrawMoveButtons(x, y, width, height);
        }

        void DrawMoveButtons(float x, float y, float width, float height)
        {
            EnsureReferenceCubeSpawnerReference();
            if (m_ReferenceCubeSpawner == null)
                return;

            var buttonWidth = (width - 12f) * 0.5f;
            var rowSpacing = 8f;

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
