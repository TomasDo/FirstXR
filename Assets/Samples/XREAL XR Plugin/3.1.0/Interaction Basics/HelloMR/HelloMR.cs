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
        bool m_DefaultToHandInput = true;

        [SerializeField]
        GameObject[] m_HandVisualizers;

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
        }

        void ApplyDefaultInputOnStart()
        {
            if (m_DefaultToHandInput)
                ChangeToHandInput();
            else
                ApplyInputVisuals(XREALPlugin.GetInputSource());
        }

        void RefreshStatusText()
        {
            if (m_TextCurrentMode == null)
                return;

            m_TextCurrentMode.text = $"Track: {XREALPlugin.GetTrackingType()}, Input: {XREALPlugin.GetInputSource()}";
        }

        static bool IsHandInput(InputSource source)
        {
            return source == InputSource.Hands || source == InputSource.ControllerAndHands;
        }

        void ApplyInputVisuals(InputSource source)
        {
            bool showHands = IsHandInput(source);
            if (m_HandVisualizers != null)
            {
                foreach (var handRoot in m_HandVisualizers)
                {
                    if (handRoot != null)
                        handRoot.SetActive(showHands);
                }
            }
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
            ApplyInputVisuals(InputSource.Controller);
            RefreshStatusText();
        }

        /// <summary>
        /// Changes the input source to hand.
        /// </summary>
        public void ChangeToHandInput()
        {
            XREALPlugin.SetInputSource(InputSource.Hands);
            ApplyInputVisuals(InputSource.Hands);
            RefreshStatusText();
        }

        void OnGUI()
        {
            if (!m_ShowBeamProInputToggle || Application.platform != RuntimePlatform.Android)
                return;

            const float width = 280f;
            const float height = 90f;
            Rect buttonRect = new Rect(Screen.width - width - 30f, 30f, width, height);

            var currentSource = XREALPlugin.GetInputSource();
            bool isHand = IsHandInput(currentSource);
            string label = isHand ? "Switch to Controller" : "Switch to Hand";

            if (GUI.Button(buttonRect, label))
            {
                if (isHand)
                    ChangeToControllerInput();
                else
                    ChangeToHandInput();
            }
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
