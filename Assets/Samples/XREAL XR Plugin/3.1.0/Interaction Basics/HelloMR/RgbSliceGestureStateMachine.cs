using System;

namespace Unity.XR.XREAL.Samples
{
    public enum RgbSliceGesturePhase
    {
        Idle = 0,
        Priming = 1,
        Active = 2,
        FrozenForHeadMotion = 3,
        WaitingForRelease = 4,
    }

    /// <summary>Pure state machine so timing and safety behavior can be editor-tested.</summary>
    public sealed class RgbSliceGestureStateMachine
    {
        readonly double m_HoldSeconds;
        readonly double m_LossTimeoutSeconds;
        readonly double m_MinStepIntervalSeconds;
        readonly float m_DeadZone;
        readonly float m_StepDistance;

        double m_PrimeStartedAt;
        double m_LastOkObservedAt = double.NegativeInfinity;
        double m_LastStepAt = double.NegativeInfinity;
        float m_AnchorY;
        bool m_RequireRelease;

        public RgbSliceGestureStateMachine(
            double holdSeconds = 0.30,
            double lossTimeoutSeconds = 0.18,
            float deadZone = 0.025f,
            float stepDistance = 0.045f,
            double minStepIntervalSeconds = 0.10)
        {
            m_HoldSeconds = Math.Max(0.05, holdSeconds);
            m_LossTimeoutSeconds = Math.Min(0.20, Math.Max(0.05, lossTimeoutSeconds));
            m_DeadZone = Math.Max(0f, deadZone);
            m_StepDistance = Math.Max(0.005f, stepDistance);
            m_MinStepIntervalSeconds = Math.Max(0.02, minStepIntervalSeconds);
        }

        public RgbSliceGesturePhase Phase { get; private set; } = RgbSliceGesturePhase.Idle;
        public ulong ControlVersion { get; private set; }

        public event Action<RgbSliceGesturePhase> PhaseChanged;
        public event Action<int, ulong> SliceStepRequested;

        public void Observe(RgbHandGestureObservation observation, double nowSeconds, bool headMoving)
        {
            if (!observation.HandDetected)
            {
                m_RequireRelease = true;
                Transition(RgbSliceGesturePhase.WaitingForRelease);
                return;
            }

            if (observation.Gesture != RgbHandGesture.Ok)
            {
                m_RequireRelease = false;
                Transition(RgbSliceGesturePhase.Idle);
                return;
            }

            m_LastOkObservedAt = nowSeconds;
            if (m_RequireRelease)
            {
                Transition(RgbSliceGesturePhase.WaitingForRelease);
                return;
            }

            var palmY = observation.PalmPosition.y;
            switch (Phase)
            {
                case RgbSliceGesturePhase.Idle:
                case RgbSliceGesturePhase.WaitingForRelease:
                    m_PrimeStartedAt = nowSeconds;
                    m_AnchorY = palmY;
                    Transition(RgbSliceGesturePhase.Priming);
                    return;

                case RgbSliceGesturePhase.Priming:
                    if (headMoving)
                    {
                        m_PrimeStartedAt = nowSeconds;
                        m_AnchorY = palmY;
                        return;
                    }
                    if (nowSeconds - m_PrimeStartedAt >= m_HoldSeconds)
                    {
                        m_AnchorY = palmY;
                        m_LastStepAt = nowSeconds;
                        Transition(RgbSliceGesturePhase.Active);
                    }
                    return;

                case RgbSliceGesturePhase.FrozenForHeadMotion:
                    m_AnchorY = palmY;
                    if (!headMoving)
                    {
                        m_LastStepAt = nowSeconds;
                        Transition(RgbSliceGesturePhase.Active);
                    }
                    return;

                case RgbSliceGesturePhase.Active:
                    if (headMoving)
                    {
                        m_AnchorY = palmY;
                        Transition(RgbSliceGesturePhase.FrozenForHeadMotion);
                        return;
                    }
                    TryEmitStep(palmY, nowSeconds);
                    return;
            }
        }

        public void Tick(double nowSeconds, bool headMoving)
        {
            if (Phase == RgbSliceGesturePhase.Idle || Phase == RgbSliceGesturePhase.WaitingForRelease)
                return;

            if (nowSeconds - m_LastOkObservedAt > m_LossTimeoutSeconds)
            {
                m_RequireRelease = true;
                Transition(RgbSliceGesturePhase.WaitingForRelease);
                return;
            }

            if (headMoving && Phase == RgbSliceGesturePhase.Active)
                Transition(RgbSliceGesturePhase.FrozenForHeadMotion);
        }

        public void ApplyNavigationTakeover(ulong controlVersion)
        {
            if (controlVersion < ControlVersion)
                return;
            ControlVersion = controlVersion;
            m_RequireRelease = true;
            Transition(RgbSliceGesturePhase.WaitingForRelease);
        }

        /// <summary>
        /// Advances the command base after the navigation side echoes an accepted XREAL command.
        /// This does not cancel a still-held gesture; the next step uses the new version.
        /// </summary>
        public void ApplyAcknowledgedControlVersion(ulong controlVersion)
        {
            if (controlVersion > ControlVersion)
                ControlVersion = controlVersion;
        }

        public void Reset(bool requireRelease = false)
        {
            m_RequireRelease = requireRelease;
            Transition(requireRelease ? RgbSliceGesturePhase.WaitingForRelease : RgbSliceGesturePhase.Idle);
        }

        public void ResetForContext()
        {
            ControlVersion = 0;
            Reset(true);
        }

        void TryEmitStep(float palmY, double nowSeconds)
        {
            if (nowSeconds - m_LastStepAt < m_MinStepIntervalSeconds)
                return;

            var delta = palmY - m_AnchorY;
            if (Math.Abs(delta) < m_DeadZone + m_StepDistance)
                return;

            // Image coordinates grow downward: hand up decrements, hand down increments.
            var step = delta < 0f ? -1 : 1;
            m_AnchorY = palmY;
            m_LastStepAt = nowSeconds;
            SliceStepRequested?.Invoke(step, ControlVersion);
        }

        void Transition(RgbSliceGesturePhase next)
        {
            if (next == Phase)
                return;
            Phase = next;
            PhaseChanged?.Invoke(next);
        }
    }
}
