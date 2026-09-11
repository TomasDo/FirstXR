using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class RgbGestureStreamingTests
    {
        [Test]
        public void LandmarkAnalyzer_RecognizesOkAndReportsPalmPosition()
        {
            var points = CreateOpenHand();
            points[4] = new Vector3(0.47f, 0.30f, 0f);
            points[8] = new Vector3(0.48f, 0.30f, 0f);
            var analyzer = new RgbHandGestureAnalyzer();

            var observation = analyzer.Analyze(new RgbHandLandmarkFrame(points, 0.9f, true, 42, 1.25));

            Assert.That(observation.Gesture, Is.EqualTo(RgbHandGesture.Ok));
            Assert.That(observation.HandDetected, Is.True);
            Assert.That(observation.SourceSequence, Is.EqualTo(42));
            Assert.That(observation.PalmPosition.y, Is.InRange(0.45f, 0.75f));
        }

        [Test]
        public void LandmarkAnalyzer_RejectsLowConfidenceOrOpenTips()
        {
            var points = CreateOpenHand();
            var analyzer = new RgbHandGestureAnalyzer();

            Assert.That(analyzer.Analyze(new RgbHandLandmarkFrame(points, 0.9f, true, 1, 0)).Gesture,
                Is.EqualTo(RgbHandGesture.None));
            points[4] = points[8];
            Assert.That(analyzer.Analyze(new RgbHandLandmarkFrame(points, 0.2f, true, 2, 0)).Gesture,
                Is.EqualTo(RgbHandGesture.None));
        }

        [Test]
        public void HeldOk_ActivatesAfter300Ms_AndStationaryHandDoesNotRepeat()
        {
            var machine = NewMachine();
            var steps = new List<int>();
            machine.SliceStepRequested += (step, _) => steps.Add(step);

            machine.Observe(Ok(0.50f), 0.00, false);
            machine.Observe(Ok(0.50f), 0.29, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Priming));
            machine.Observe(Ok(0.50f), 0.30, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Active));

            machine.Observe(Ok(0.40f), 0.41, false);
            machine.Observe(Ok(0.40f), 0.70, false);

            CollectionAssert.AreEqual(new[] { -1 }, steps);
        }

        [Test]
        public void ReleaseStopsImmediately_AndFrameLossStopsWithin200Ms()
        {
            var machine = NewMachine();
            machine.Observe(Ok(0.5f), 0.00, false);
            machine.Observe(Ok(0.5f), 0.30, false);
            machine.Observe(Released(), 0.31, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Idle));

            machine.Observe(Ok(0.5f), 1.00, false);
            machine.Observe(Ok(0.5f), 1.30, false);
            machine.Tick(1.49, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));
            machine.Observe(Ok(0.4f), 1.50, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));
            machine.Observe(Released(), 1.51, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Idle));
        }

        [Test]
        public void ExplicitTrackingLossRequiresReleaseBeforeRearming()
        {
            var machine = NewMachine();
            machine.Observe(Ok(0.5f), 0.00, false);
            machine.Observe(Ok(0.5f), 0.30, false);
            machine.Observe(RgbHandGestureObservation.None, 0.31, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));
            machine.Observe(Ok(0.4f), 0.32, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));

            var released = RgbHandGestureObservation.None;
            released.HandDetected = true;
            machine.Observe(released, 0.33, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Idle));
        }

        [Test]
        public void NavigationTakeoverCancelsOldGestureAndCarriesNewVersion()
        {
            var machine = NewMachine();
            var versions = new List<ulong>();
            machine.SliceStepRequested += (_, version) => versions.Add(version);
            machine.Observe(Ok(0.5f), 0.00, false);
            machine.Observe(Ok(0.5f), 0.30, false);

            machine.ApplyNavigationTakeover(7);
            machine.Observe(Ok(0.3f), 0.50, false);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));
            machine.Observe(Released(), 0.51, false);
            machine.Observe(Ok(0.5f), 0.60, false);
            machine.Observe(Ok(0.5f), 0.90, false);
            machine.Observe(Ok(0.6f), 1.01, false);

            CollectionAssert.AreEqual(new ulong[] { 7 }, versions);
        }

        [Test]
        public void XrealEchoAdvancesVersionWithoutCancellingHeldGesture()
        {
            var machine = NewMachine();
            var versions = new List<ulong>();
            machine.SliceStepRequested += (_, version) => versions.Add(version);
            machine.Observe(Ok(0.5f), 0.00, false);
            machine.Observe(Ok(0.5f), 0.30, false);
            machine.Observe(Ok(0.4f), 0.41, false);

            machine.ApplyAcknowledgedControlVersion(8);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.Active));
            machine.Observe(Ok(0.5f), 0.52, false);

            CollectionAssert.AreEqual(new ulong[] { 0, 8 }, versions);
        }

        [Test]
        public void NewContextClearsOldControlVersionAndRequiresRelease()
        {
            var machine = NewMachine();
            machine.ApplyNavigationTakeover(12);
            machine.ResetForContext();

            Assert.That(machine.ControlVersion, Is.Zero);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.WaitingForRelease));
        }

        [Test]
        public void HeadMotionFreezesAndRebasesBeforeAnotherStep()
        {
            var machine = NewMachine();
            var steps = 0;
            machine.SliceStepRequested += (_, __) => steps++;
            machine.Observe(Ok(0.5f), 0.00, false);
            machine.Observe(Ok(0.5f), 0.30, false);
            machine.Observe(Ok(0.3f), 0.41, true);
            Assert.That(machine.Phase, Is.EqualTo(RgbSliceGesturePhase.FrozenForHeadMotion));
            machine.Observe(Ok(0.3f), 0.50, false);
            machine.Observe(Ok(0.3f), 0.70, false);
            Assert.That(steps, Is.Zero);
            machine.Observe(Ok(0.4f), 0.81, false);
            Assert.That(steps, Is.EqualTo(1));
        }

        [Test]
        public void RuntimeProviderIsExplicitlyUnavailableWithoutRegisteredBridge()
        {
            RgbHandLandmarkProviderRegistry.ClearFactory();
            using (var provider = RgbHandLandmarkProviderRegistry.CreateProvider())
            {
                Assert.That(provider.IsAvailable, Is.False);
                StringAssert.Contains("MediaPipe", provider.Status);
            }
        }

        static RgbSliceGestureStateMachine NewMachine()
        {
            return new RgbSliceGestureStateMachine(0.30, 0.18, 0.02f, 0.04f, 0.10);
        }

        static RgbHandGestureObservation Ok(float y)
        {
            return new RgbHandGestureObservation
            {
                Gesture = RgbHandGesture.Ok,
                HandDetected = true,
                PalmPosition = new Vector2(0.5f, y),
                Confidence = 1f,
            };
        }

        static RgbHandGestureObservation Released()
        {
            var observation = RgbHandGestureObservation.None;
            observation.HandDetected = true;
            return observation;
        }

        static Vector3[] CreateOpenHand()
        {
            var p = new Vector3[RgbHandLandmarkFrame.LandmarkCount];
            p[0] = new Vector3(0.50f, 0.80f);
            p[1] = new Vector3(0.43f, 0.70f);
            p[2] = new Vector3(0.39f, 0.60f);
            p[3] = new Vector3(0.35f, 0.50f);
            p[4] = new Vector3(0.30f, 0.40f);
            p[5] = new Vector3(0.43f, 0.62f);
            p[6] = new Vector3(0.43f, 0.48f);
            p[7] = new Vector3(0.43f, 0.34f);
            p[8] = new Vector3(0.43f, 0.20f);
            p[9] = new Vector3(0.50f, 0.60f);
            p[10] = new Vector3(0.50f, 0.44f);
            p[11] = new Vector3(0.50f, 0.30f);
            p[12] = new Vector3(0.50f, 0.16f);
            p[13] = new Vector3(0.57f, 0.62f);
            p[14] = new Vector3(0.57f, 0.47f);
            p[15] = new Vector3(0.57f, 0.34f);
            p[16] = new Vector3(0.57f, 0.22f);
            p[17] = new Vector3(0.63f, 0.65f);
            p[18] = new Vector3(0.64f, 0.52f);
            p[19] = new Vector3(0.65f, 0.42f);
            p[20] = new Vector3(0.66f, 0.32f);
            return p;
        }
    }
}
