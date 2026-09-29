using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Small, allocation-free-per-frame counters for the RGB pause field trial.
    /// Reports one aggregate line every 30 seconds instead of logging each frame.
    /// </summary>
    internal sealed class DentalPerformanceDiagnostics : MonoBehaviour
    {
        const float ReportIntervalSeconds = 30f;
        const float AgeSampleIntervalSeconds = 0.1f;
        static volatile DentalPerformanceDiagnostics s_Instance;

        readonly float[] m_FrameMs = new float[8192];
        readonly float[] m_AgeMs = new float[1024];
        readonly DentalNavigationFlowMetrics m_NavigationFlow = new DentalNavigationFlowMetrics();
        int m_FrameCount;
        int m_AgeCount;
        int m_FrameOver50;
        int m_FrameOver100;
        int m_FrameOver200;
        int m_AgeOver200;
        int m_AgeOver500;
        int m_DelayEntries;
        int m_DelayExits;
        int m_InterruptionEntries;
        bool m_HaveWarningState;
        bool m_LastDelay;
        bool m_LastInterruption;
        bool m_SkipNextFrame;
        float m_WindowStart;
        float m_NextAgeSample;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isEditor || s_Instance != null)
                return;

            var host = new GameObject("Dental Performance Diagnostics");
            DontDestroyOnLoad(host);
            host.AddComponent<DentalPerformanceDiagnostics>();
        }

        void Awake()
        {
            s_Instance = this;
            ResetWindow(Time.realtimeSinceStartup);
            Debug.Log($"DentalPerf: monitoring frame time and navigation age; RGB enabled={RgbFeaturePolicy.Enabled}");
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        // Called by the gRPC reader. The collector owns a short lock and fixed
        // buffers, so no Unity API or per-navigation-frame allocation runs here.
        internal static void RecordNavigationReceived(
            float receiveGapMs,
            bool hasReceiveGap,
            float sourceAgeMs,
            bool hasValidSourceTime,
            ulong skippedSequences,
            bool overwrotePending)
        {
            s_Instance?.m_NavigationFlow.RecordReceived(
                receiveGapMs, hasReceiveGap, sourceAgeMs, hasValidSourceTime,
                skippedSequences, overwrotePending);
        }

        internal static void RecordNavigationRejected()
        {
            s_Instance?.m_NavigationFlow.RecordRejected();
        }

        internal static void RecordNavigationApplied(float receiveToApplyMs, bool accepted)
        {
            s_Instance?.m_NavigationFlow.RecordApplied(receiveToApplyMs, accepted);
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                ResetWindow(Time.realtimeSinceStartup);
                m_NavigationFlow.Reset();
                m_SkipNextFrame = true;
            }
        }

        void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now - m_WindowStart >= ReportIntervalSeconds)
            {
                Report(now - m_WindowStart);
                ResetWindow(now);
            }

            var frameMs = Time.unscaledDeltaTime * 1000f;
            if (m_SkipNextFrame)
                m_SkipNextFrame = false;
            else if (frameMs > 0f && m_FrameCount < m_FrameMs.Length)
            {
                m_FrameMs[m_FrameCount++] = frameMs;
                if (frameMs > 50f) m_FrameOver50++;
                if (frameMs > 100f) m_FrameOver100++;
                if (frameMs > 200f) m_FrameOver200++;
            }

            var state = DentalNavigationState.Instance;
            if (state == null)
                return;

            var evaluation = state.LastEvaluation;
            var delayed = evaluation.ShowAlarm && evaluation.AlarmText == "导航数据延迟";
            var interrupted = evaluation.ShowAlarm && evaluation.AlarmText == "导航数据中断";
            if (m_HaveWarningState)
            {
                if (delayed && !m_LastDelay) m_DelayEntries++;
                if (!delayed && m_LastDelay) m_DelayExits++;
                if (interrupted && !m_LastInterruption) m_InterruptionEntries++;
            }
            m_HaveWarningState = true;
            m_LastDelay = delayed;
            m_LastInterruption = interrupted;

            if (now < m_NextAgeSample)
                return;

            m_NextAgeSample = now + AgeSampleIntervalSeconds;
            var snapshot = state.Capture(now);
            if (!snapshot.HasNavigationFrame || float.IsInfinity(snapshot.AgeSeconds) || m_AgeCount >= m_AgeMs.Length)
                return;

            var ageMs = snapshot.AgeSeconds * 1000f;
            m_AgeMs[m_AgeCount++] = ageMs;
            if (ageMs > 200f) m_AgeOver200++;
            if (ageMs > 500f) m_AgeOver500++;
        }

        void ResetWindow(float now)
        {
            m_WindowStart = now;
            m_NextAgeSample = now;
            m_FrameCount = m_AgeCount = 0;
            m_FrameOver50 = m_FrameOver100 = m_FrameOver200 = 0;
            m_AgeOver200 = m_AgeOver500 = 0;
            m_DelayEntries = m_DelayExits = m_InterruptionEntries = 0;
        }

        void Report(float seconds)
        {
            Array.Sort(m_FrameMs, 0, m_FrameCount);
            Array.Sort(m_AgeMs, 0, m_AgeCount);
            var flow = m_NavigationFlow.CaptureAndReset();
            Debug.Log(
                $"DentalPerf: window={seconds:0.0}s frame n={m_FrameCount} " +
                $"p50={Percentile(m_FrameMs, m_FrameCount, 0.50f):0.0}ms " +
                $"p95={Percentile(m_FrameMs, m_FrameCount, 0.95f):0.0}ms " +
                $"max={Maximum(m_FrameMs, m_FrameCount):0.0}ms >50/100/200={m_FrameOver50}/{m_FrameOver100}/{m_FrameOver200}; " +
                $"age n={m_AgeCount} p50={Percentile(m_AgeMs, m_AgeCount, 0.50f):0.0}ms " +
                $"p95={Percentile(m_AgeMs, m_AgeCount, 0.95f):0.0}ms " +
                $"max={Maximum(m_AgeMs, m_AgeCount):0.0}ms >200/500={m_AgeOver200}/{m_AgeOver500}; " +
                $"delay enter/exit={m_DelayEntries}/{m_DelayExits} interruption enter={m_InterruptionEntries}; " +
                $"flow recv={flow.Received} applied={flow.Applied} rejected={flow.Rejected} " +
                $"overwritten={flow.Overwritten} seqSkip={flow.SkippedSequences} " +
                $"sourceInvalid={flow.InvalidSourceTime} " +
                $"recvGap n={flow.ReceiveGap.Count} p95={flow.ReceiveGap.P95:0.0}ms max={flow.ReceiveGap.Max:0.0}ms " +
                $"sourceAge n={flow.SourceAge.Count} p95={flow.SourceAge.P95:0.0}ms max={flow.SourceAge.Max:0.0}ms " +
                $"recvToApply n={flow.ReceiveToApply.Count} p95={flow.ReceiveToApply.P95:0.0}ms max={flow.ReceiveToApply.Max:0.0}ms");
        }

        static float Percentile(float[] sorted, int count, float fraction)
        {
            return count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(count * fraction) - 1, 0, count - 1)];
        }

        static float Maximum(float[] sorted, int count)
        {
            return count == 0 ? 0f : sorted[count - 1];
        }
    }
}
