using System;
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

    [Serializable]
    public struct DentalToleranceSettings
    {
        public float LateralGreenMm;
        public float LateralRedMm;
        public float LateralHysteresisMm;
        public float AngleGreenDeg;
        public float AngleRedDeg;
        public float AngleHysteresisDeg;
        public float DepthApproachMm;
        public float DepthHysteresisMm;
        public float AgingSeconds;
        public float StaleSeconds;
        public float HideSeconds;

        public static DentalToleranceSettings Default => new DentalToleranceSettings
        {
            LateralGreenMm = 0.50f,
            LateralRedMm = 1.00f,
            LateralHysteresisMm = 0.10f,
            AngleGreenDeg = 2f,
            AngleRedDeg = 5f,
            AngleHysteresisDeg = 0.40f,
            DepthApproachMm = 1.00f,
            DepthHysteresisMm = 0.10f,
            AgingSeconds = 0.20f,
            StaleSeconds = 0.50f,
            HideSeconds = 1.00f,
        };
    }

    public sealed class DentalNavigationBand
    {
        DentalMetricGrade m_Depth = DentalMetricGrade.Unavailable;
        DentalMetricGrade m_Lateral = DentalMetricGrade.Unavailable;
        DentalMetricGrade m_Angle = DentalMetricGrade.Unavailable;

        public DentalHudEvaluation Evaluate(DentalNavigationSnapshot snap, DentalToleranceSettings t)
        {
            if (t.HideSeconds <= 0f)
                t = DentalToleranceSettings.Default;

            if (snap.Link == DentalLinkState.Lost || snap.Link == DentalLinkState.Idle)
                return Unavailable("未连接机器人", dashNumbers: true, showAge: false);

            if (snap.Link == DentalLinkState.Connecting && !snap.HasMetadata)
                return Unavailable(string.Empty, dashNumbers: true, showAge: false);

            if (!snap.HasMetadata || snap.AgeSeconds > t.HideSeconds)
                return Unavailable("导航数据中断", dashNumbers: true, showAge: false);

            if (snap.AgeSeconds > t.StaleSeconds)
            {
                m_Depth = m_Lateral = m_Angle = DentalMetricGrade.Stale;
                return new DentalHudEvaluation
                {
                    Depth = DentalMetricGrade.Stale,
                    Lateral = DentalMetricGrade.Stale,
                    Angle = DentalMetricGrade.Stale,
                    Overall = DentalMetricGrade.Stale,
                    ShowAlarm = true,
                    AlarmText = "导航数据中断",
                    ShowAge = true,
                    DashNumbers = false,
                };
            }

            m_Lateral = StepLowerIsBetter(m_Lateral, snap.LateralMm, t.LateralGreenMm, t.LateralRedMm, t.LateralHysteresisMm);
            m_Angle = StepLowerIsBetter(m_Angle, snap.AngleDeg, t.AngleGreenDeg, t.AngleRedDeg, t.AngleHysteresisDeg);
            m_Depth = StepDepth(m_Depth, snap.DepthMm, t.DepthApproachMm, t.DepthHysteresisMm);

            var overall = CombineOverall(m_Lateral, m_Angle, m_Depth);
            var alarm = string.Empty;
            if (m_Depth == DentalMetricGrade.Red)
                alarm = "超过目标深度";
            else if (overall == DentalMetricGrade.Red)
                alarm = "超差  停针并核对";

            return new DentalHudEvaluation
            {
                Depth = m_Depth,
                Lateral = m_Lateral,
                Angle = m_Angle,
                Overall = overall,
                ShowAlarm = !string.IsNullOrEmpty(alarm),
                AlarmText = alarm,
                ShowAge = snap.AgeSeconds > t.AgingSeconds,
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

        static DentalMetricGrade StepLowerIsBetter(
            DentalMetricGrade previous, float value, float greenMax, float redMin, float hysteresis)
        {
            DentalMetricGrade raw;
            if (value <= greenMax)
                raw = DentalMetricGrade.Green;
            else if (value <= redMin)
                raw = DentalMetricGrade.Amber;
            else
                raw = DentalMetricGrade.Red;

            if (previous == DentalMetricGrade.Red && value > redMin - hysteresis)
                return DentalMetricGrade.Red;

            if (previous == DentalMetricGrade.Amber && value > greenMax - hysteresis)
                return raw == DentalMetricGrade.Red ? DentalMetricGrade.Red : DentalMetricGrade.Amber;

            return raw;
        }

        static DentalMetricGrade StepDepth(
            DentalMetricGrade previous, float depthMm, float approachMm, float hysteresis)
        {
            DentalMetricGrade raw;
            if (depthMm < 0f)
                raw = DentalMetricGrade.Red;
            else if (depthMm <= approachMm)
                raw = DentalMetricGrade.Amber;
            else
                raw = DentalMetricGrade.Neutral;

            if (previous == DentalMetricGrade.Red && depthMm <= hysteresis)
                return DentalMetricGrade.Red;

            if (previous == DentalMetricGrade.Amber && depthMm <= approachMm + hysteresis && raw != DentalMetricGrade.Red)
                return DentalMetricGrade.Amber;

            return raw;
        }

        static DentalMetricGrade CombineOverall(
            DentalMetricGrade lateral, DentalMetricGrade angle, DentalMetricGrade depth)
        {
            var overall = DentalMetricGrade.Green;
            if (lateral == DentalMetricGrade.Amber || angle == DentalMetricGrade.Amber)
                overall = DentalMetricGrade.Amber;
            if (lateral == DentalMetricGrade.Red || angle == DentalMetricGrade.Red || depth == DentalMetricGrade.Red)
                overall = DentalMetricGrade.Red;
            return overall;
        }
    }
}
