namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Central switch for RGB camera features during navigation performance diagnosis.
    /// Change this one value to restore RGB capture, preview, gestures, and streaming.
    /// </summary>
    public static class RgbFeaturePolicy
    {
        public static bool Enabled => false;
        public const string DisabledMessage = "RGB 已暂停";
    }
}
