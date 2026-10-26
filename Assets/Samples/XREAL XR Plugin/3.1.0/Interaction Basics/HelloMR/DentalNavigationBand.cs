using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public enum DentalMetricGrade
    {
        Unavailable = 0,
        Neutral = 1,
        Green = 2,
        Amber = 3,
        Red = 4,
        Stale = 5,
    }

    public struct DentalHudEvaluation
    {
        public DentalMetricGrade Depth;
        public DentalMetricGrade Lateral;
        public DentalMetricGrade Angle;
        public DentalMetricGrade Overall;
        public bool ShowAlarm;
        public string AlarmText;
        public bool ShowAge;
        public bool DashNumbers;
    }

    /// <summary>
    /// Applies only thresholds received for the current navigation context. There
    /// are deliberately no built-in clinical limits in the glasses application.
    /// </summary>
    public sealed class DentalNavigationBand
    {
        public const float AgingSeconds = 0.20f;
        public const float HideSeconds = 0.50f;

        DentalMetricGrade m_Depth = DentalMetricGrade.Unavailable;
        DentalMetricGrade m_Lateral = DentalMetricGrade.Unavailable;
        DentalMetricGrade m_Angle = DentalMetricGrade.Unavailable;
        ulong m_LastContextVersion;
        ulong m_LastThresholdVersion;

        public DentalHudEvaluation Evaluate(DentalNavigationSnapshot snap)
        {
            if (snap.ContextVersion != m_LastContextVersion
                || (snap.HasThresholds && snap.Thresholds.ConfigVersion != m_LastThresholdVersion))
            {
                m_Depth = m_Lateral = m_Angle = DentalMetricGrade.Unavailable;
                m_LastContextVersion = snap.ContextVersion;
                m_LastThresholdVersion = snap.HasThresholds ? snap.Thresholds.ConfigVersion : 0;
            }

            if (snap.Link == DentalLinkState.Lost || snap.Link == DentalLinkState.Idle)
                return Unavailable("Not connected to navigation software", true, false);

            if (snap.Link == DentalLinkState.Connecting && !snap.HasMetadata)
                return Unavailable(string.Empty, true, false);

            if (!snap.HasMetadata)
                return Unavailable(string.IsNullOrEmpty(snap.InvalidReason) ? "Waiting for navigation data" : snap.InvalidReason,
                    true, false);

            if (snap.AgeSeconds > HideSeconds)
                return Unavailable("Navigation data interrupted", true, false);

            if (snap.AgeSeconds > AgingSeconds)
                return Stale("Navigation data delayed", false);

            if (!snap.HasNavigationFrame)
                return Unavailable("Navigation protocol upgrade required: direction and depth definitions missing", false, false);

            if (!snap.FrameValid)
                return Unavailable(string.IsNullOrEmpty(snap.InvalidReason) ? "Invalid navigation data" : snap.InvalidReason,
                    true, false);

            if (!snap.HasLateralDirection || !snap.HasTiltDirection || !snap.HasDepthBreakdown)
                return Unavailable("Navigation protocol upgrade required: direction and depth definitions missing", false, false);

            if (!snap.HasThresholds || !ThresholdsAreValid(snap.Thresholds, snap.ContextVersion))
                return Unavailable("Thresholds not synced", false, false);

            var t = snap.Thresholds;
            var inclusive = t.BoundaryRule == DentalThresholdBoundaryRule.UpperBoundsInclusive;
            m_Lateral = StepLowerIsBetter(m_Lateral, snap.LateralMm, t.LateralGreenMaxMm,
                t.LateralRedMinMm, t.LateralHysteresisMm, inclusive);
            m_Angle = StepLowerIsBetter(m_Angle, snap.AngleDeg, t.AngleGreenMaxDeg,
                t.AngleRedMinDeg, t.AngleHysteresisDeg, inclusive);
            m_Depth = StepDepth(m_Depth, snap.RemainingDepthMm, t.DepthApproachMm,
                t.DepthAtTargetToleranceMm, t.DepthOverrunRedMm, t.DepthHysteresisMm);

            var overall = CombineOverall(m_Lateral, m_Angle, m_Depth);
            var alarm = AlarmFor(snap, m_Lateral, m_Angle, m_Depth);
            return new DentalHudEvaluation
            {
                Depth = m_Depth,
                Lateral = m_Lateral,
                Angle = m_Angle,
                Overall = overall,
                ShowAlarm = !string.IsNullOrEmpty(alarm),
                AlarmText = alarm,
                ShowAge = false,
                DashNumbers = false,
            };
        }

        DentalHudEvaluation Unavailable(string alarm, bool dashNumbers, bool showAge)
        {
            m_Depth = m_Lateral = m_Angle = DentalMetricGrade.Unavailable;
            return new DentalHudEvaluation
            {
                Depth = DentalMetricGrade.Unavailable,
                Lateral = DentalMetricGrade.Unavailable,
                Angle = DentalMetricGrade.Unavailable,
                Overall = DentalMetricGrade.Unavailable,
                ShowAlarm = !string.IsNullOrEmpty(alarm),
                AlarmText = alarm ?? string.Empty,
                ShowAge = showAge,
                DashNumbers = dashNumbers,
            };
        }

        DentalHudEvaluation Stale(string alarm, bool dashNumbers)
        {
            m_Depth = m_Lateral = m_Angle = DentalMetricGrade.Stale;
            return new DentalHudEvaluation
            {
                Depth = DentalMetricGrade.Stale,
                Lateral = DentalMetricGrade.Stale,
                Angle = DentalMetricGrade.Stale,
                Overall = DentalMetricGrade.Stale,
                ShowAlarm = true,
                AlarmText = alarm,
                ShowAge = true,
                DashNumbers = dashNumbers,
            };
        }

        static bool ThresholdsAreValid(DentalNavigationThresholds t, ulong contextVersion)
        {
            return t.ContextVersion == contextVersion && t.IsValid;
        }

        static DentalMetricGrade StepLowerIsBetter(DentalMetricGrade previous, float value,
            float greenMax, float redMin, float hysteresis, bool inclusive)
        {
            var magnitude = Mathf.Abs(value);
            var isGreen = inclusive ? magnitude <= greenMax : magnitude < greenMax;
            var isAmber = inclusive ? magnitude <= redMin : magnitude < redMin;
            var raw = isGreen
                ? DentalMetricGrade.Green
                : isAmber ? DentalMetricGrade.Amber : DentalMetricGrade.Red;

            if (previous == DentalMetricGrade.Red && magnitude > redMin - hysteresis)
                return DentalMetricGrade.Red;
            if (previous == DentalMetricGrade.Amber && magnitude > greenMax - hysteresis)
                return raw == DentalMetricGrade.Red ? DentalMetricGrade.Red : DentalMetricGrade.Amber;
            return raw;
        }

        static DentalMetricGrade StepDepth(DentalMetricGrade previous, float remainingMm,
            float approachMm, float atTargetToleranceMm, float overrunRedMm, float hysteresisMm)
        {
            DentalMetricGrade raw;
            if (remainingMm < -overrunRedMm)
                raw = DentalMetricGrade.Red;
            else if (remainingMm <= approachMm || Mathf.Abs(remainingMm) <= atTargetToleranceMm)
                raw = DentalMetricGrade.Amber;
            else
                raw = DentalMetricGrade.Neutral;

            if (previous == DentalMetricGrade.Red && remainingMm < -overrunRedMm + hysteresisMm)
                return DentalMetricGrade.Red;
            if (previous == DentalMetricGrade.Amber && remainingMm <= approachMm + hysteresisMm
                && raw != DentalMetricGrade.Red)
                return DentalMetricGrade.Amber;
            return raw;
        }

        static DentalMetricGrade CombineOverall(DentalMetricGrade lateral, DentalMetricGrade angle,
            DentalMetricGrade depth)
        {
            if (lateral == DentalMetricGrade.Red || angle == DentalMetricGrade.Red || depth == DentalMetricGrade.Red)
                return DentalMetricGrade.Red;
            if (lateral == DentalMetricGrade.Amber || angle == DentalMetricGrade.Amber || depth == DentalMetricGrade.Amber)
                return DentalMetricGrade.Amber;
            return DentalMetricGrade.Green;
        }

        static string AlarmFor(DentalNavigationSnapshot snap, DentalMetricGrade lateral,
            DentalMetricGrade angle, DentalMetricGrade depth)
        {
            if (depth == DentalMetricGrade.Red && snap.RemainingDepthMm < 0f)
                return "Target depth exceeded by " + Mathf.Abs(snap.RemainingDepthMm).ToString("0.0") + " mm";
            if (lateral == DentalMetricGrade.Red && angle == DentalMetricGrade.Red)
                return "Position and angle exceed thresholds";
            if (lateral == DentalMetricGrade.Red)
                return "Position exceeds threshold";
            if (angle == DentalMetricGrade.Red)
                return "Angle exceeds threshold";
            return string.Empty;
        }
    }
}
