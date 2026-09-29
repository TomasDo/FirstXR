using System;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Thread-safe, fixed-memory measurements from gRPC receipt to navigation
    /// application. Its per-frame record methods do not allocate.
    /// </summary>
    public sealed class DentalNavigationFlowMetrics
    {
        const int SampleCapacity = 4096;

        readonly object m_Gate = new object();
        readonly float[] m_ReceiveGapMs = new float[SampleCapacity];
        readonly float[] m_SourceAgeMs = new float[SampleCapacity];
        readonly float[] m_ReceiveToApplyMs = new float[SampleCapacity];
        int m_ReceiveGapCount;
        int m_SourceAgeCount;
        int m_ReceiveToApplyCount;
        int m_Received;
        int m_Applied;
        int m_Rejected;
        int m_Overwritten;
        int m_InvalidSourceTime;
        ulong m_SkippedSequences;

        public void RecordReceived(
            float receiveGapMs,
            bool hasReceiveGap,
            float sourceAgeMs,
            bool hasValidSourceTime,
            ulong skippedSequences,
            bool overwrotePending)
        {
            lock (m_Gate)
            {
                m_Received++;
                if (hasReceiveGap && IsFiniteNonnegative(receiveGapMs)
                    && m_ReceiveGapCount < m_ReceiveGapMs.Length)
                    m_ReceiveGapMs[m_ReceiveGapCount++] = receiveGapMs;

                // Keep the signed offset. A negative age can reveal clock skew;
                // clamping it would conceal the source of the stale warning.
                if (hasValidSourceTime && IsFinite(sourceAgeMs))
                {
                    if (m_SourceAgeCount < m_SourceAgeMs.Length)
                        m_SourceAgeMs[m_SourceAgeCount++] = sourceAgeMs;
                }
                else
                {
                    m_InvalidSourceTime++;
                }

                if (ulong.MaxValue - m_SkippedSequences < skippedSequences)
                    m_SkippedSequences = ulong.MaxValue;
                else
                    m_SkippedSequences += skippedSequences;
                if (overwrotePending)
                    m_Overwritten++;
            }
        }

        public void RecordRejected()
        {
            lock (m_Gate)
                m_Rejected++;
        }

        public void RecordApplied(float receiveToApplyMs, bool accepted)
        {
            lock (m_Gate)
            {
                if (!accepted)
                {
                    m_Rejected++;
                    return;
                }

                m_Applied++;
                if (IsFiniteNonnegative(receiveToApplyMs)
                    && m_ReceiveToApplyCount < m_ReceiveToApplyMs.Length)
                    m_ReceiveToApplyMs[m_ReceiveToApplyCount++] = receiveToApplyMs;
            }
        }

        public DentalNavigationFlowSnapshot CaptureAndReset()
        {
            lock (m_Gate)
            {
                Array.Sort(m_ReceiveGapMs, 0, m_ReceiveGapCount);
                Array.Sort(m_SourceAgeMs, 0, m_SourceAgeCount);
                Array.Sort(m_ReceiveToApplyMs, 0, m_ReceiveToApplyCount);
                var result = new DentalNavigationFlowSnapshot(
                    m_Received, m_Applied, m_Rejected, m_Overwritten,
                    m_InvalidSourceTime, m_SkippedSequences,
                    Summarize(m_ReceiveGapMs, m_ReceiveGapCount),
                    Summarize(m_SourceAgeMs, m_SourceAgeCount),
                    Summarize(m_ReceiveToApplyMs, m_ReceiveToApplyCount));
                ResetWithoutLock();
                return result;
            }
        }

        public void Reset()
        {
            lock (m_Gate)
                ResetWithoutLock();
        }

        void ResetWithoutLock()
        {
            m_ReceiveGapCount = m_SourceAgeCount = m_ReceiveToApplyCount = 0;
            m_Received = m_Applied = m_Rejected = m_Overwritten = m_InvalidSourceTime = 0;
            m_SkippedSequences = 0;
        }

        static DentalNavigationMetricSummary Summarize(float[] sorted, int count)
        {
            if (count == 0)
                return new DentalNavigationMetricSummary(0, 0f, 0f);

            var p95Index = Math.Max(0, (int)Math.Ceiling(count * 0.95) - 1);
            return new DentalNavigationMetricSummary(count, sorted[p95Index], sorted[count - 1]);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFiniteNonnegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }
    }

    public readonly struct DentalNavigationFlowSnapshot
    {
        public readonly int Received;
        public readonly int Applied;
        public readonly int Rejected;
        public readonly int Overwritten;
        public readonly int InvalidSourceTime;
        public readonly ulong SkippedSequences;
        public readonly DentalNavigationMetricSummary ReceiveGap;
        public readonly DentalNavigationMetricSummary SourceAge;
        public readonly DentalNavigationMetricSummary ReceiveToApply;

        public DentalNavigationFlowSnapshot(
            int received, int applied, int rejected, int overwritten,
            int invalidSourceTime, ulong skippedSequences,
            DentalNavigationMetricSummary receiveGap,
            DentalNavigationMetricSummary sourceAge,
            DentalNavigationMetricSummary receiveToApply)
        {
            Received = received;
            Applied = applied;
            Rejected = rejected;
            Overwritten = overwritten;
            InvalidSourceTime = invalidSourceTime;
            SkippedSequences = skippedSequences;
            ReceiveGap = receiveGap;
            SourceAge = sourceAge;
            ReceiveToApply = receiveToApply;
        }
    }

    public readonly struct DentalNavigationMetricSummary
    {
        public readonly int Count;
        public readonly float P95;
        public readonly float Max;

        public DentalNavigationMetricSummary(int count, float p95, float max)
        {
            Count = count;
            P95 = p95;
            Max = max;
        }
    }
}
