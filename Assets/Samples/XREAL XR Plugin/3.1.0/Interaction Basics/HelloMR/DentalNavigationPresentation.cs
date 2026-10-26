namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Shared anatomical direction labels and Beam Pro navigation summaries.
    /// </summary>
    internal static class DentalNavigationPresentation
    {
        public static string DirectionPhrase(float buccal, float mesial, string prefix, string suffix = "")
        {
            const float epsilon = 0.02f;
            var first = buccal > epsilon ? "buccal" : buccal < -epsilon ? "lingual" : string.Empty;
            var second = mesial > epsilon ? "mesial" : mesial < -epsilon ? "distal" : string.Empty;
            var direction = string.IsNullOrEmpty(first) ? second
                : string.IsNullOrEmpty(second) ? first
                : first + " / " + second;
            return string.IsNullOrEmpty(direction) ? "Centered" : prefix + direction + suffix;
        }

        public static string BeamProOverallPhrase(DentalMetricGrade grade)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green:
                    return "Within range";
                case DentalMetricGrade.Amber:
                    return "Near limit";
                case DentalMetricGrade.Red:
                    return "Out of range";
                case DentalMetricGrade.Stale:
                    return "Stale data";
                default:
                    return "No data";
            }
        }
    }
}
