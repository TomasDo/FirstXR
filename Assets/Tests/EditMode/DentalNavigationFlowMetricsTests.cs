using NUnit.Framework;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalNavigationFlowMetricsTests
    {
        [Test]
        public void SummarizesReceiveSourceAndApplyStagesThenResetsAtomically()
        {
            var metrics = new DentalNavigationFlowMetrics();
            for (var i = 1; i <= 20; i++)
            {
                metrics.RecordReceived(i, true, i * 10f, true,
                    i == 20 ? 3UL : 0UL, i % 5 == 0);
                metrics.RecordApplied(i * 2f, true);
            }
            metrics.RecordRejected();

            var report = metrics.CaptureAndReset();
            Assert.That(report.Received, Is.EqualTo(20));
            Assert.That(report.Applied, Is.EqualTo(20));
            Assert.That(report.Rejected, Is.EqualTo(1));
            Assert.That(report.Overwritten, Is.EqualTo(4));
            Assert.That(report.SkippedSequences, Is.EqualTo(3));
            Assert.That(report.ReceiveGap.Count, Is.EqualTo(20));
            Assert.That(report.ReceiveGap.P95, Is.EqualTo(19f));
            Assert.That(report.ReceiveGap.Max, Is.EqualTo(20f));
            Assert.That(report.SourceAge.P95, Is.EqualTo(190f));
            Assert.That(report.SourceAge.Max, Is.EqualTo(200f));
            Assert.That(report.ReceiveToApply.P95, Is.EqualTo(38f));
            Assert.That(report.ReceiveToApply.Max, Is.EqualTo(40f));

            var next = metrics.CaptureAndReset();
            Assert.That(next.Received, Is.Zero);
            Assert.That(next.ReceiveGap.Count, Is.Zero);
            Assert.That(next.SourceAge.Count, Is.Zero);
            Assert.That(next.ReceiveToApply.Count, Is.Zero);
        }

        [Test]
        public void PreservesSignedClockOffsetAndExcludesInvalidSourceTimes()
        {
            var metrics = new DentalNavigationFlowMetrics();
            metrics.RecordReceived(0f, false, -80f, true, 0, false);
            metrics.RecordReceived(0f, false, 0f, false, 0, false);
            metrics.RecordApplied(4f, false);

            var report = metrics.CaptureAndReset();
            Assert.That(report.Received, Is.EqualTo(2));
            Assert.That(report.Applied, Is.Zero);
            Assert.That(report.Rejected, Is.EqualTo(1));
            Assert.That(report.InvalidSourceTime, Is.EqualTo(1));
            Assert.That(report.ReceiveGap.Count, Is.Zero);
            Assert.That(report.SourceAge.Count, Is.EqualTo(1));
            Assert.That(report.SourceAge.P95, Is.EqualTo(-80f));
            Assert.That(report.SourceAge.Max, Is.EqualTo(-80f));
            Assert.That(report.ReceiveToApply.Count, Is.Zero);
        }
    }
}
