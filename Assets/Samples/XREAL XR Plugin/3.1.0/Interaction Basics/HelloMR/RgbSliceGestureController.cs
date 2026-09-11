using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public sealed class RgbSliceStepEventArgs : EventArgs
    {
        public RgbSliceStepEventArgs(int step, ulong controlVersion, double requestedAtSeconds)
        {
            Step = step;
            ControlVersion = controlVersion;
            RequestedAtSeconds = requestedAtSeconds;
        }

        public int Step { get; }
        public ulong ControlVersion { get; }
        public double RequestedAtSeconds { get; }
    }

    /// <summary>
    /// Turns a held OK pose and vertical palm displacement into versioned slice-step requests.
    /// CT/navigation code subscribes to the event; this component does not own CT state.
    /// </summary>
    public sealed class RgbSliceGestureController : MonoBehaviour
    {
        [SerializeField] double m_HoldSeconds = 0.30;
        [SerializeField] double m_LossTimeoutSeconds = 0.18;
        [SerializeField] float m_DeadZone = 0.025f;
        [SerializeField] float m_StepDistance = 0.045f;
        [SerializeField] double m_MinStepIntervalSeconds = 0.10;
        [SerializeField] float m_HeadAngularSpeedThreshold = 35f;
        [SerializeField] float m_HeadLinearSpeedThreshold = 0.15f;

        static RgbSliceGestureController s_Instance;
        RgbHandGestureRecognizer m_Recognizer;
        RgbSliceGestureStateMachine m_StateMachine;
        Transform m_Head;
        Vector3 m_PreviousHeadPosition;
        Quaternion m_PreviousHeadRotation;
        double m_PreviousHeadSampleTime;
        bool m_HasHeadSample;
        bool m_HeadMoving;

        public static RgbSliceGestureController Instance => s_Instance;
        public RgbSliceGesturePhase Phase => m_StateMachine != null ? m_StateMachine.Phase : RgbSliceGesturePhase.Idle;
        public ulong ControlVersion => m_StateMachine != null ? m_StateMachine.ControlVersion : 0;
        public bool IsAdjusting => Phase == RgbSliceGesturePhase.Active;

        public event EventHandler<RgbSliceStepEventArgs> SliceStepRequested;
        public event Action<RgbSliceGesturePhase> PhaseChanged;
        public static event Action<RgbSliceStepEventArgs> AnySliceStepRequested;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
            s_Instance = this;
            m_StateMachine = new RgbSliceGestureStateMachine(
                m_HoldSeconds, m_LossTimeoutSeconds, m_DeadZone, m_StepDistance, m_MinStepIntervalSeconds);
            m_StateMachine.SliceStepRequested += OnStateMachineStep;
            m_StateMachine.PhaseChanged += OnStateMachinePhaseChanged;
        }

        void Start()
        {
            BindRecognizer();
            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            m_Head = camera != null ? camera.transform : null;
        }

        void OnEnable()
        {
            BindRecognizer();
        }

        void OnDisable()
        {
            if (m_Recognizer != null)
                m_Recognizer.ObservationUpdated -= OnObservation;
            m_StateMachine?.Reset(true);
        }

        void OnDestroy()
        {
            if (m_Recognizer != null)
                m_Recognizer.ObservationUpdated -= OnObservation;
            if (m_StateMachine != null)
            {
                m_StateMachine.SliceStepRequested -= OnStateMachineStep;
                m_StateMachine.PhaseChanged -= OnStateMachinePhaseChanged;
            }
            if (s_Instance == this)
                s_Instance = null;
        }

        void Update()
        {
            if (m_Recognizer == null)
                BindRecognizer();
            UpdateHeadMotion();
            m_StateMachine.Tick(Time.realtimeSinceStartupAsDouble, m_HeadMoving);
        }

        /// <summary>
        /// Called when the navigation workstation takes authority. Equal/newer versions
        /// cancel the gesture immediately and require OK to be released before rearming.
        /// </summary>
        public void ApplyNavigationTakeover(ulong controlVersion)
        {
            m_StateMachine.ApplyNavigationTakeover(controlVersion);
        }

        public void ApplyAcknowledgedControlVersion(ulong controlVersion)
        {
            m_StateMachine.ApplyAcknowledgedControlVersion(controlVersion);
        }

        public void CancelGesture(bool requireRelease = true)
        {
            m_StateMachine.Reset(requireRelease);
        }

        public void ResetForContext()
        {
            m_StateMachine.ResetForContext();
        }

        void BindRecognizer()
        {
            var recognizer = RgbHandGestureRecognizer.Instance;
            if (recognizer == null)
                recognizer = FindObjectOfType<RgbHandGestureRecognizer>();
            if (recognizer == m_Recognizer)
                return;
            if (m_Recognizer != null)
                m_Recognizer.ObservationUpdated -= OnObservation;
            m_Recognizer = recognizer;
            if (m_Recognizer != null)
                m_Recognizer.ObservationUpdated += OnObservation;
        }

        void OnObservation(RgbHandGestureObservation observation)
        {
            m_StateMachine.Observe(observation, Time.realtimeSinceStartupAsDouble, m_HeadMoving);
        }

        void UpdateHeadMotion()
        {
            if (m_Head == null)
            {
                var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
                m_Head = camera != null ? camera.transform : null;
            }
            if (m_Head == null)
            {
                m_HeadMoving = false;
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (!m_HasHeadSample)
            {
                m_HasHeadSample = true;
                m_PreviousHeadPosition = m_Head.position;
                m_PreviousHeadRotation = m_Head.rotation;
                m_PreviousHeadSampleTime = now;
                return;
            }

            var dt = Math.Max(0.001, now - m_PreviousHeadSampleTime);
            var angularSpeed = Quaternion.Angle(m_PreviousHeadRotation, m_Head.rotation) / (float)dt;
            var linearSpeed = Vector3.Distance(m_PreviousHeadPosition, m_Head.position) / (float)dt;
            m_HeadMoving = angularSpeed >= m_HeadAngularSpeedThreshold || linearSpeed >= m_HeadLinearSpeedThreshold;
            m_PreviousHeadPosition = m_Head.position;
            m_PreviousHeadRotation = m_Head.rotation;
            m_PreviousHeadSampleTime = now;
        }

        void OnStateMachineStep(int step, ulong controlVersion)
        {
            var args = new RgbSliceStepEventArgs(step, controlVersion, Time.realtimeSinceStartupAsDouble);
            SliceStepRequested?.Invoke(this, args);
            AnySliceStepRequested?.Invoke(args);
        }

        void OnStateMachinePhaseChanged(RgbSliceGesturePhase phase)
        {
            PhaseChanged?.Invoke(phase);
            var text = phase == RgbSliceGesturePhase.Active ? "切片调整中" :
                phase == RgbSliceGesturePhase.FrozenForHeadMotion ? "头部移动，切片已冻结" :
                phase == RgbSliceGesturePhase.WaitingForRelease ? "请松开 OK 手势后重试" :
                phase == RgbSliceGesturePhase.Priming ? "保持 OK 手势" : "待机";
            BeamProUnifiedLogWindow.SetStatus("切片手势", text);
        }
    }
}
