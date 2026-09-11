using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Classifies an OK pose from MediaPipe-compatible 21-point hand landmarks.
    /// It contains no image/skin-color fallback, so gloves are handled by the selected
    /// landmark model rather than hard-coded color thresholds.
    /// </summary>
    public sealed class RgbHandGestureAnalyzer
    {
        const int Wrist = 0;
        const int ThumbTip = 4;
        const int IndexTip = 8;
        const int MiddleMcp = 9;
        const int MiddlePip = 10;
        const int MiddleTip = 12;
        const int RingMcp = 13;
        const int RingPip = 14;
        const int RingTip = 16;
        const int PinkyMcp = 17;
        const int PinkyPip = 18;
        const int PinkyTip = 20;

        readonly float m_OkTipDistanceRatio;
        readonly float m_MinConfidence;

        public RgbHandGestureAnalyzer(float okTipDistanceRatio = 0.34f, float minConfidence = 0.55f)
        {
            m_OkTipDistanceRatio = Mathf.Max(0.05f, okTipDistanceRatio);
            m_MinConfidence = Mathf.Clamp01(minConfidence);
        }

        public RgbHandGestureObservation Analyze(RgbHandLandmarkFrame frame)
        {
            if (!frame.IsValid || frame.Confidence < m_MinConfidence)
            {
                var missing = RgbHandGestureObservation.None;
                missing.SourceSequence = frame.SourceSequence;
                missing.ObservedAtSeconds = frame.ObservedAtSeconds;
                return missing;
            }

            var points = frame.Landmarks;
            var palmScale = Vector2.Distance(points[Wrist], points[MiddleMcp]);
            if (palmScale < 0.001f)
                return RgbHandGestureObservation.None;

            var thumbIndexDistance = Vector2.Distance(points[ThumbTip], points[IndexTip]);
            var extendedCount = 0;
            if (IsFingerExtended(points, MiddleMcp, MiddlePip, MiddleTip)) extendedCount++;
            if (IsFingerExtended(points, RingMcp, RingPip, RingTip)) extendedCount++;
            if (IsFingerExtended(points, PinkyMcp, PinkyPip, PinkyTip)) extendedCount++;

            var palm = (points[Wrist] + points[5] + points[MiddleMcp] + points[RingMcp] + points[PinkyMcp]) / 5f;
            var isOk = thumbIndexDistance <= palmScale * m_OkTipDistanceRatio && extendedCount >= 2;

            return new RgbHandGestureObservation
            {
                Gesture = isOk ? RgbHandGesture.Ok : RgbHandGesture.None,
                HandDetected = true,
                PalmPosition = new Vector2(palm.x, palm.y),
                Confidence = frame.Confidence,
                PeakSeparation = thumbIndexDistance / palmScale,
                SourceSequence = frame.SourceSequence,
                ObservedAtSeconds = frame.ObservedAtSeconds,
            };
        }

        static bool IsFingerExtended(Vector3[] points, int mcp, int pip, int tip)
        {
            var wrist = new Vector2(points[Wrist].x, points[Wrist].y);
            var mcpDistance = Vector2.Distance(wrist, points[mcp]);
            var pipDistance = Vector2.Distance(wrist, points[pip]);
            var tipDistance = Vector2.Distance(wrist, points[tip]);
            return pipDistance > mcpDistance && tipDistance > pipDistance * 1.05f;
        }
    }
}
