using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Classifies simple poses from MediaPipe-compatible 21-point hand landmarks.
    /// One call reads one frame and stores no history. Pass the previous observation's
    /// <see cref="RgbHandGestureLatch"/> to keep a pinch or a finger posture through the
    /// uncertain band. There is no image or skin-color fallback.
    /// </summary>
    public sealed class RgbHandGestureAnalyzer
    {
        const int Wrist = 0;
        const int ThumbTip = 4;
        const int IndexMcp = 5;
        const int IndexPip = 6;
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

        const int IndexFinger = 0;
        const int MiddleFinger = 1;
        const int RingFinger = 2;
        const int PinkyFinger = 3;

        const float PinchEnterRatio = 0.32f;
        const float PinchExitRatio = 0.45f;
        const float ExtendedAngle = 150f;
        const float CurledAngle = 110f;
        const float TwoFingerSpreadAngle = 18f;
        const float MinimumSegmentLengthSquared = 1e-8f;

        readonly float m_OkTipDistanceRatio;
        readonly float m_MinConfidence;

        public RgbHandGestureAnalyzer(float okTipDistanceRatio = 0.34f, float minConfidence = 0.55f)
        {
            m_OkTipDistanceRatio = Mathf.Max(0.05f, okTipDistanceRatio);
            m_MinConfidence = Mathf.Clamp01(minConfidence);
        }

        public RgbHandGestureObservation Analyze(RgbHandLandmarkFrame frame)
        {
            return Analyze(frame, default);
        }

        public RgbHandGestureObservation Analyze(RgbHandLandmarkFrame frame, RgbHandGestureLatch latch)
        {
            if (!frame.IsValid || frame.Confidence < m_MinConfidence)
                return Missing(frame);

            var points = frame.Landmarks;
            var palmScale = Vector3.Distance(GeometryPoint(frame, Wrist), GeometryPoint(frame, MiddleMcp));
            if (palmScale < 0.001f)
                return Missing(frame);

            var pinchRatio = Vector3.Distance(GeometryPoint(frame, ThumbTip), GeometryPoint(frame, IndexTip)) / palmScale;
            var index = UpdatePosture(frame, IndexMcp, IndexPip, IndexTip, ReadPosture(latch, IndexFinger));
            var middle = UpdatePosture(frame, MiddleMcp, MiddlePip, MiddleTip, ReadPosture(latch, MiddleFinger));
            var ring = UpdatePosture(frame, RingMcp, RingPip, RingTip, ReadPosture(latch, RingFinger));
            var pinky = UpdatePosture(frame, PinkyMcp, PinkyPip, PinkyTip, ReadPosture(latch, PinkyFinger));

            var extendedCount = 0;
            if (middle == FingerPosture.Extended) extendedCount++;
            if (ring == FingerPosture.Extended) extendedCount++;
            if (pinky == FingerPosture.Extended) extendedCount++;

            var pinchLatched = latch.PinchLatched
                ? pinchRatio <= PinchExitRatio
                : pinchRatio < PinchEnterRatio;
            var isOk = extendedCount >= 2
                && (pinchRatio < m_OkTipDistanceRatio || (latch.PinchLatched && pinchLatched));

            RgbHandGesture gesture;
            if (isOk)
                gesture = RgbHandGesture.Ok;
            else if (pinchLatched)
                gesture = RgbHandGesture.Pinch;
            else if (IsTwoFinger(frame, index, middle, ring, pinky))
                gesture = RgbHandGesture.TwoFinger;
            else if (IsOpenPalm(index, middle, ring, pinky))
                gesture = RgbHandGesture.OpenPalm;
            else
                gesture = RgbHandGesture.None;

            var nextLatch = new RgbHandGestureLatch
            {
                PinchLatched = pinchLatched || isOk,
            };
            WritePosture(ref nextLatch, IndexFinger, index);
            WritePosture(ref nextLatch, MiddleFinger, middle);
            WritePosture(ref nextLatch, RingFinger, ring);
            WritePosture(ref nextLatch, PinkyFinger, pinky);

            var palm = (points[Wrist] + points[IndexMcp] + points[MiddleMcp] + points[RingMcp] + points[PinkyMcp]) / 5f;
            return new RgbHandGestureObservation
            {
                Gesture = gesture,
                HandDetected = true,
                PalmPosition = new Vector2(palm.x, palm.y),
                Confidence = frame.Confidence,
                PeakSeparation = pinchRatio,
                SourceSequence = frame.SourceSequence,
                ObservedAtSeconds = frame.ObservedAtSeconds,
                Latch = nextLatch,
            };
        }

        static bool IsOpenPalm(FingerPosture index, FingerPosture middle, FingerPosture ring, FingerPosture pinky)
        {
            return index == FingerPosture.Extended
                && middle == FingerPosture.Extended
                && ring == FingerPosture.Extended
                && pinky == FingerPosture.Extended;
        }

        static bool IsTwoFinger(
            RgbHandLandmarkFrame frame,
            FingerPosture index,
            FingerPosture middle,
            FingerPosture ring,
            FingerPosture pinky)
        {
            if (index != FingerPosture.Extended
                || middle != FingerPosture.Extended
                || ring != FingerPosture.Curled
                || pinky != FingerPosture.Curled)
                return false;

            return TrySpreadAngle(GeometryPoint(frame, IndexMcp), GeometryPoint(frame, IndexTip),
                GeometryPoint(frame, MiddleMcp), GeometryPoint(frame, MiddleTip), out var spread)
                && spread > TwoFingerSpreadAngle;
        }

        static Vector3 GeometryPoint(RgbHandLandmarkFrame frame, int index)
        {
            // MediaPipe normalizes x/z by width and y by height. Use width units for
            // geometry, while leaving the original image coordinates for PalmPosition.
            var point = frame.Landmarks[index];
            point.y /= frame.ImageAspectRatio;
            return point;
        }

        static FingerPosture UpdatePosture(RgbHandLandmarkFrame frame, int mcp, int pip, int tip, FingerPosture previous)
        {
            if (!TryJointAngle(GeometryPoint(frame, mcp), GeometryPoint(frame, pip), GeometryPoint(frame, tip), out var angle))
                return previous;
            if (angle >= ExtendedAngle)
                return FingerPosture.Extended;
            if (angle <= CurledAngle)
                return FingerPosture.Curled;
            return previous;
        }

        static bool TryJointAngle(Vector3 mcp, Vector3 pip, Vector3 tip, out float angle)
        {
            var towardMcp = mcp - pip;
            var towardTip = tip - pip;
            if (towardMcp.sqrMagnitude < MinimumSegmentLengthSquared
                || towardTip.sqrMagnitude < MinimumSegmentLengthSquared)
            {
                angle = 0f;
                return false;
            }

            angle = Vector3.Angle(towardMcp, towardTip);
            return true;
        }

        static bool TrySpreadAngle(
            Vector3 indexMcp,
            Vector3 indexTip,
            Vector3 middleMcp,
            Vector3 middleTip,
            out float angle)
        {
            var indexDirection = indexTip - indexMcp;
            var middleDirection = middleTip - middleMcp;
            if (indexDirection.sqrMagnitude < MinimumSegmentLengthSquared
                || middleDirection.sqrMagnitude < MinimumSegmentLengthSquared)
            {
                angle = 0f;
                return false;
            }

            angle = Vector3.Angle(indexDirection, middleDirection);
            return true;
        }

        static FingerPosture ReadPosture(RgbHandGestureLatch latch, int finger)
        {
            var bit = (byte)(1 << finger);
            if ((latch.ExtendedBits & bit) != 0)
                return FingerPosture.Extended;
            if ((latch.CurledBits & bit) != 0)
                return FingerPosture.Curled;
            return FingerPosture.Unknown;
        }

        static void WritePosture(ref RgbHandGestureLatch latch, int finger, FingerPosture posture)
        {
            var bit = (byte)(1 << finger);
            latch.ExtendedBits = (byte)(latch.ExtendedBits & ~bit);
            latch.CurledBits = (byte)(latch.CurledBits & ~bit);
            if (posture == FingerPosture.Extended)
                latch.ExtendedBits |= bit;
            else if (posture == FingerPosture.Curled)
                latch.CurledBits |= bit;
        }

        static RgbHandGestureObservation Missing(RgbHandLandmarkFrame frame)
        {
            var missing = RgbHandGestureObservation.None;
            missing.SourceSequence = frame.SourceSequence;
            missing.ObservedAtSeconds = frame.ObservedAtSeconds;
            return missing;
        }

        enum FingerPosture
        {
            Unknown = 0,
            Extended = 1,
            Curled = 2,
        }
    }
}
