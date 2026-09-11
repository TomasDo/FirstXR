using System;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>Gestures recognized from offline hand landmarks.</summary>
    public enum RgbHandGesture
    {
        None = 0,
        OpenPalm = 1,
        Fist = 2,
        Pinch = 3,
        Ok = 4,
    }

    public static class RgbHandGestureNames
    {
        public static string ToChinese(RgbHandGesture gesture)
        {
            switch (gesture)
            {
                case RgbHandGesture.OpenPalm:
                    return "张开";
                case RgbHandGesture.Fist:
                    return "握拳";
                case RgbHandGesture.Pinch:
                    return "捏合";
                case RgbHandGesture.Ok:
                    return "OK";
                default:
                    return "无";
            }
        }
    }

    public struct RgbHandGestureObservation
    {
        public RgbHandGesture Gesture;
        public int DefectCount;
        public int PeakCount;
        public float Compactness;
        public float AreaRatio;
        public float PeakSeparation;
        public bool HasHole;
        public bool HandDetected;
        public UnityEngine.Vector2 PalmPosition;
        public float Confidence;
        public long SourceSequence;
        public double ObservedAtSeconds;

        public static RgbHandGestureObservation None => new RgbHandGestureObservation
        {
            Gesture = RgbHandGesture.None,
        };
    }

    public sealed class RgbHandGestureChangedEventArgs : EventArgs
    {
        public RgbHandGestureChangedEventArgs(RgbHandGesture previous, RgbHandGesture current)
        {
            Previous = previous;
            Current = current;
        }

        public RgbHandGesture Previous { get; }
        public RgbHandGesture Current { get; }
    }
}
