using NUnit.Framework;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalWifiLowLatencyLeaseTests
    {
        sealed class FakeLock : DentalWifiLowLatencyLease.ILock
        {
            public int AcquireCalls;
            public int ReleaseCalls;
            public int DisposeCalls;
            public bool AcquireSucceeds = true;
            public bool FailNextRelease;

            public bool TryAcquire()
            {
                AcquireCalls++;
                return AcquireSucceeds;
            }

            public bool TryRelease()
            {
                ReleaseCalls++;
                if (!FailNextRelease)
                    return true;
                FailNextRelease = false;
                return false;
            }

            public void Dispose() => DisposeCalls++;
        }

        [Test]
        public void FramePollingOnlyCallsPlatformOnDemandChanges()
        {
            var platform = new FakeLock();
            var lease = new DentalWifiLowLatencyLease(platform);
            for (var i = 0; i < 100; i++) lease.SetRequired(false);
            Assert.That(platform.AcquireCalls, Is.Zero);
            Assert.That(platform.ReleaseCalls, Is.Zero);
            for (var i = 0; i < 100; i++) lease.SetRequired(true);
            Assert.That(platform.AcquireCalls, Is.EqualTo(1));
            for (var i = 0; i < 100; i++) lease.SetRequired(false);
            Assert.That(platform.ReleaseCalls, Is.EqualTo(1));
            lease.Dispose();
        }

        [Test]
        public void LifecycleReleaseAllowsResumeToAcquireAgain()
        {
            var platform = new FakeLock();
            using (var lease = new DentalWifiLowLatencyLease(platform))
            {
                lease.SetRequired(true);
                lease.Release();
                lease.SetRequired(false);
                Assert.That(platform.ReleaseCalls, Is.EqualTo(1));
                lease.SetRequired(true);
                Assert.That(platform.AcquireCalls, Is.EqualTo(2));
            }
            Assert.That(platform.ReleaseCalls, Is.EqualTo(2));
            Assert.That(platform.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public void FailedReleaseWaitsForLifecycleEventBeforeRetrying()
        {
            var platform = new FakeLock { FailNextRelease = true };
            using (var lease = new DentalWifiLowLatencyLease(platform))
            {
                lease.SetRequired(true);
                lease.SetRequired(false);
                for (var i = 0; i < 100; i++) lease.SetRequired(false);
                Assert.That(platform.ReleaseCalls, Is.EqualTo(1), "Do not retry JNI every frame.");
                lease.Release();
                Assert.That(platform.ReleaseCalls, Is.EqualTo(2), "Pause or disconnect retries cleanup.");
                lease.SetRequired(true);
                Assert.That(platform.AcquireCalls, Is.EqualTo(2));
            }
        }

        [Test]
        public void FailedReleasePreservesHeldStateUntilCleanupSucceeds()
        {
            var platform = new FakeLock { FailNextRelease = true };
            using (var lease = new DentalWifiLowLatencyLease(platform))
            {
                lease.SetRequired(true);
                lease.SetRequired(false);
                lease.SetRequired(true);
                Assert.That(platform.AcquireCalls, Is.EqualTo(1), "A failed release may still hold the lock.");
                lease.SetRequired(false);
                Assert.That(platform.ReleaseCalls, Is.EqualTo(2));
            }
        }

        [Test]
        public void AcquisitionFailureDoesNotRetryPerFrameAndStillAllowsCleanup()
        {
            var platform = new FakeLock { AcquireSucceeds = false };
            using (var lease = new DentalWifiLowLatencyLease(platform))
            {
                for (var i = 0; i < 100; i++) lease.SetRequired(true);
                Assert.That(platform.AcquireCalls, Is.EqualTo(1));
                lease.SetRequired(false);
                Assert.That(platform.ReleaseCalls, Is.EqualTo(1), "Stream completion also cleans up a partially completed acquire.");
            }
        }

        [Test]
        public void DisposeRetriesFailedCleanupAndPreventsFurtherAcquisition()
        {
            var platform = new FakeLock { FailNextRelease = true };
            var lease = new DentalWifiLowLatencyLease(platform);
            lease.SetRequired(true);
            lease.Release();
            lease.Dispose();
            lease.Dispose();
            lease.SetRequired(true);
            lease.Release();
            Assert.That(platform.AcquireCalls, Is.EqualTo(1));
            Assert.That(platform.ReleaseCalls, Is.EqualTo(2));
            Assert.That(platform.DisposeCalls, Is.EqualTo(1));
        }
    }
}
