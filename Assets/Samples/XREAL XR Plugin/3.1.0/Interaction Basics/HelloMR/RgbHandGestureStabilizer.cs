namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Confirms a latched gesture for <see cref="ConfirmSeconds"/> before publishing it.
    /// A lost hand or a low-confidence frame clears the published gesture immediately.
    /// The raw, unlatched observation is returned separately so OK slice control keeps
    /// its existing one-frame rule.
    /// </summary>
    public sealed class RgbHandGestureStabilizer
    {
        public const double ConfirmSeconds = 0.25;

        RgbHandGestureLatch m_Latch;
        RgbHandGesture m_Candidate;
        double m_CandidateSince;
        bool m_HasCandidate;

        public RgbHandGesture Published { get; private set; } = RgbHandGesture.None;

        public bool Observe(
            RgbHandGestureAnalyzer analyzer,
            RgbHandLandmarkFrame frame,
            double nowSeconds,
            out RgbHandGestureObservation rawObservation)
        {
            rawObservation = analyzer.Analyze(frame);
            if (!rawObservation.HandDetected)
            {
                m_Latch = default;
                m_Candidate = RgbHandGesture.None;
                m_HasCandidate = false;
                return SetPublished(RgbHandGesture.None);
            }

            var shaped = analyzer.Analyze(frame, m_Latch);
            m_Latch = shaped.Latch;
            if (!m_HasCandidate || shaped.Gesture != m_Candidate)
            {
                m_Candidate = shaped.Gesture;
                m_CandidateSince = nowSeconds;
                m_HasCandidate = true;
            }

            if (nowSeconds - m_CandidateSince < ConfirmSeconds)
                return false;
            return SetPublished(m_Candidate);
        }

        public void Reset()
        {
            m_Latch = default;
            m_Candidate = RgbHandGesture.None;
            m_HasCandidate = false;
            m_CandidateSince = 0d;
            Published = RgbHandGesture.None;
        }

        bool SetPublished(RgbHandGesture gesture)
        {
            if (gesture == Published)
                return false;
            Published = gesture;
            return true;
        }
    }
}
