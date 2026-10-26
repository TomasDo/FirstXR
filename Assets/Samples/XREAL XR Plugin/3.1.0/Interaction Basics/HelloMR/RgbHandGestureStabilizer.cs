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
        public const double StaleSeconds = 0.18;

        RgbHandGestureLatch m_Latch;
        RgbHandGesture m_Candidate;
        double m_CandidateSince;
        bool m_HasCandidate;
        double m_LastObservationTime = double.NegativeInfinity;

        public RgbHandGesture Published { get; private set; } = RgbHandGesture.None;

        public bool Observe(
            RgbHandGestureAnalyzer analyzer,
            RgbHandLandmarkFrame frame,
            double nowSeconds,
            out RgbHandGestureObservation rawObservation)
        {
            var previous = Published;
            // Check the gap before accepting the new observation, including after a
            // main-thread pause where no per-frame expiry check could run.
            Expire(nowSeconds);
            rawObservation = analyzer.Analyze(frame);
            if (!rawObservation.HandDetected)
            {
                Reset();
                return Published != previous;
            }

            m_LastObservationTime = nowSeconds;
            var shaped = analyzer.Analyze(frame, m_Latch);
            m_Latch = shaped.Latch;
            if (!m_HasCandidate || shaped.Gesture != m_Candidate)
            {
                m_Candidate = shaped.Gesture;
                m_CandidateSince = nowSeconds;
                m_HasCandidate = true;
            }

            if (nowSeconds - m_CandidateSince >= ConfirmSeconds)
                Published = m_Candidate;
            return Published != previous;
        }

        /// <summary>Clears both pending and published poses when observations stop.</summary>
        public bool Expire(double nowSeconds)
        {
            if (nowSeconds - m_LastObservationTime <= StaleSeconds)
                return false;
            var changed = Published != RgbHandGesture.None;
            Reset();
            return changed;
        }

        public void Reset()
        {
            m_Latch = default;
            m_Candidate = RgbHandGesture.None;
            m_HasCandidate = false;
            m_CandidateSince = 0d;
            m_LastObservationTime = double.NegativeInfinity;
            Published = RgbHandGesture.None;
        }
    }
}
