using System;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalNavigationBandTests
    {
        GameObject m_Host;
        DentalNavigationState m_State;
        DentalNavigationBand m_Band;

        [SetUp]
        public void SetUp()
        {
            m_Host = new GameObject("DentalNavigationBandTests");
            m_State = m_Host.AddComponent<DentalNavigationState>();
            m_Band = new DentalNavigationBand();
            Assert.That(m_State.ApplyNavigationContext(Context()), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Host != null)
                UnityEngine.Object.DestroyImmediate(m_Host);
        }

        [Test]
        public void MissingThresholdsNeverShowGreenButKeepMeasuredNumbers()
        {
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 0.1f, 0.1f, 4f)), Is.True);

            var result = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));

            Assert.That(result.Overall, Is.EqualTo(DentalMetricGrade.Unavailable));
            Assert.That(result.AlarmText, Is.EqualTo("阈值未同步"));
            Assert.That(result.DashNumbers, Is.False);
        }

        [Test]
        public void PositionAndAngleAreEvaluatedIndependently()
        {
            Assert.That(m_State.ApplyThresholds(Thresholds(DentalThresholdBoundaryRule.UpperBoundsInclusive)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 1.2f, 1.0f, 4f)), Is.True);

            var result = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));

            Assert.That(result.Lateral, Is.EqualTo(DentalMetricGrade.Red));
            Assert.That(result.Angle, Is.EqualTo(DentalMetricGrade.Green));
            Assert.That(result.Depth, Is.EqualTo(DentalMetricGrade.Neutral));
            Assert.That(result.Overall, Is.EqualTo(DentalMetricGrade.Red));
            Assert.That(result.AlarmText, Is.EqualTo("位置超出阈值"));
        }

        [Test]
        public void InclusiveAndExclusiveRulesDifferExactlyAtBoundary()
        {
            Assert.That(m_State.ApplyThresholds(Thresholds(DentalThresholdBoundaryRule.UpperBoundsInclusive, 1)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 0.5f, 2.0f, 3f)), Is.True);
            var inclusive = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));
            Assert.That(inclusive.Lateral, Is.EqualTo(DentalMetricGrade.Green));
            Assert.That(inclusive.Angle, Is.EqualTo(DentalMetricGrade.Green));

            Assert.That(m_State.ApplyThresholds(Thresholds(DentalThresholdBoundaryRule.UpperBoundsExclusive, 2)), Is.True);
            var exclusive = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));
            Assert.That(exclusive.Lateral, Is.EqualTo(DentalMetricGrade.Amber));
            Assert.That(exclusive.Angle, Is.EqualTo(DentalMetricGrade.Amber));
        }

        [Test]
        public void NegativeRemainingDepthPreservesSmallOverrunAndEscalatesAtConfiguredLimit()
        {
            Assert.That(m_State.ApplyThresholds(Thresholds(DentalThresholdBoundaryRule.UpperBoundsInclusive)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 0.1f, 0.1f, -0.3f)), Is.True);
            var smallOverrun = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));
            Assert.That(smallOverrun.Depth, Is.EqualTo(DentalMetricGrade.Amber));

            Assert.That(m_State.ApplyNavigationFrame(Frame(2, 0.1f, 0.1f, -0.6f)), Is.True);
            var configuredOverrun = m_Band.Evaluate(m_State.Capture(Time.realtimeSinceStartup));
            Assert.That(configuredOverrun.Depth, Is.EqualTo(DentalMetricGrade.Red));
            Assert.That(configuredOverrun.AlarmText, Does.Contain("0.6 mm"));
        }

        [TestCase(0.25f, false, "导航数据延迟")]
        [TestCase(0.75f, true, "导航数据中断")]
        public void FrameAgingAndRecoveryKeepSessionConnectedWhileRetainingNavigationWarnings(
            float age, bool hideNumbers, string warning)
        {
            Assert.That(m_State.ApplyThresholds(Thresholds(DentalThresholdBoundaryRule.UpperBoundsInclusive)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 0.1f, 0.1f, 4f)), Is.True);
            var fresh = m_State.Capture(Time.realtimeSinceStartup);
            AssertConnected(fresh);
            Assert.That(m_Band.Evaluate(fresh).ShowAlarm, Is.False);

            var aged = m_State.Capture(Time.realtimeSinceStartup + age);
            var warningEvaluation = m_Band.Evaluate(aged);
            AssertConnected(aged);
            Assert.That(warningEvaluation.ShowAlarm, Is.True);
            Assert.That(warningEvaluation.AlarmText, Is.EqualTo(warning));
            Assert.That(warningEvaluation.DashNumbers, Is.EqualTo(hideNumbers));
            Assert.That(aged.HideNumbers, Is.EqualTo(hideNumbers));

            Assert.That(m_State.ApplyNavigationFrame(Frame(2, 0.1f, 0.1f, 4f)), Is.True);
            var recovered = m_State.Capture(Time.realtimeSinceStartup);
            var recoveredEvaluation = m_Band.Evaluate(recovered);
            AssertConnected(recovered);
            Assert.That(recoveredEvaluation.ShowAlarm, Is.False);
            Assert.That(recoveredEvaluation.DashNumbers, Is.False);
        }

        [Test]
        public void WaitingForFirstFrameKeepsConnectionAndShowsWaitingWarning()
        {
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            var evaluation = m_Band.Evaluate(snapshot);

            AssertConnected(snapshot);
            Assert.That(evaluation.ShowAlarm, Is.True);
            Assert.That(evaluation.AlarmText, Is.EqualTo("等待导航数据"));
            Assert.That(evaluation.DashNumbers, Is.True);
        }

        [Test]
        public void InvalidTrackingFrameKeepsConnectionButPreservesItsWarningAndHidesNumbers()
        {
            var invalid = new DentalNavigationFrameData(
                "session", 1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false, "跟踪无效",
                Vector3.zero, Vector3.forward, false, Matrix4x4.identity,
                0, 0, 0, 0, 0, 0, 0, 10, 10,
                true, true, true);
            Assert.That(m_State.ApplyNavigationFrame(invalid), Is.True);
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            var evaluation = m_Band.Evaluate(snapshot);

            AssertConnected(snapshot);
            Assert.That(evaluation.ShowAlarm, Is.True);
            Assert.That(evaluation.AlarmText, Is.EqualTo("跟踪无效"));
            Assert.That(evaluation.DashNumbers, Is.True);
        }

        [Test]
        public void StoppedNavigationKeepsSessionConnectedAndShowsTheStopReason()
        {
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 0.1f, 0.1f, 4f)), Is.True);
            m_State.NotifyNavigationStopped("导航已暂停");
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            var evaluation = m_Band.Evaluate(snapshot);

            AssertConnected(snapshot);
            Assert.That(evaluation.ShowAlarm, Is.True);
            Assert.That(evaluation.AlarmText, Is.EqualTo("导航已暂停"));
            Assert.That(evaluation.DashNumbers, Is.True);
        }

        [Test]
        public void ActualConnectionEventsChangeIndicatorAndKeepNumbersHidden()
        {
            m_State.NotifyConnecting("127.0.0.1:50051");
            var connecting = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(DentalConnectionPresentation.Label(connecting.Link), Is.EqualTo("连接中"));
            Assert.That(DentalConnectionPresentation.IndicatorColor(connecting.Link, Color.green, Color.yellow, Color.red),
                Is.EqualTo(Color.yellow));
            Assert.That(m_Band.Evaluate(connecting).DashNumbers, Is.True);

            m_State.NotifyDisconnected("数据流已关闭");
            var disconnected = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(DentalConnectionPresentation.Label(disconnected.Link), Is.EqualTo("未连接"));
            Assert.That(DentalConnectionPresentation.IndicatorColor(disconnected.Link, Color.green, Color.yellow, Color.red),
                Is.EqualTo(Color.red));
            var evaluation = m_Band.Evaluate(disconnected);
            Assert.That(evaluation.AlarmText, Is.EqualTo("未连接导航软件"));
            Assert.That(evaluation.DashNumbers, Is.True);
        }

        static void AssertConnected(DentalNavigationSnapshot snapshot)
        {
            Assert.That(snapshot.Link, Is.EqualTo(DentalLinkState.Live));
            Assert.That(DentalConnectionPresentation.Label(snapshot.Link), Is.EqualTo("已连接"));
            Assert.That(DentalConnectionPresentation.IndicatorColor(snapshot.Link, Color.green, Color.yellow, Color.red),
                Is.EqualTo(Color.green));
        }

        static DentalNavigationContext Context()
        {
            return new DentalNavigationContext(
                "session", "case", "dataset", "ct", "plan", "36", "drill", "step", 1,
                Vector3.zero, Vector3.forward, 10f, Vector3.up, Vector3.left,
                "DICOM_PATIENT_LPS", true, Matrix4x4.identity);
        }

        static DentalNavigationThresholds Thresholds(
            DentalThresholdBoundaryRule rule,
            ulong configVersion = 1)
        {
            return new DentalNavigationThresholds(
                "session", 1, configVersion,
                0.5f, 1.0f, 0f,
                2.0f, 5.0f, 0f,
                1.0f, 0.2f, 0.5f, 0f,
                rule);
        }

        static DentalNavigationFrameData Frame(
            ulong sequence,
            float lateralMm,
            float angleDeg,
            float remainingDepthMm)
        {
            return new DentalNavigationFrameData(
                "session", 1, sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                Vector3.zero, Vector3.forward, true, Matrix4x4.identity,
                lateralMm, lateralMm, 0f,
                angleDeg, angleDeg, 0f,
                10f - remainingDepthMm, 10f, remainingDepthMm,
                true, true, true);
        }
    }
}
