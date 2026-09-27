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
        public void LandmarkAnalyzer_ClassifiesOpenPalmPinchAndTwoFinger()
        {
            var analyzer = new RgbHandGestureAnalyzer();

            Assert.That(analyzer.Analyze(Frame(CreateOpenHand(), 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
            Assert.That(analyzer.Analyze(Frame(CreateSpreadOpenPalm(), 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
            Assert.That(analyzer.Analyze(Frame(CreatePinch(0.04f), 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.Pinch));
            Assert.That(analyzer.Analyze(Frame(CreateTwoFinger(), 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.TwoFinger));
        }

        [Test]
        public void LandmarkAnalyzer_UsesDepthSoImageOverlapIsNotPinch()
        {
            var points = CreateOpenHand();
            points[4] = points[8];
            points[4].z = 0.20f;
            var analyzer = new RgbHandGestureAnalyzer();

            Assert.That(analyzer.Analyze(Frame(points, 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
        }

        [TestCase(16f / 9f, 0f)]
        [TestCase(16f / 9f, 90f)]
        [TestCase(9f / 16f, 0f)]
        [TestCase(9f / 16f, 90f)]
        public void LandmarkAnalyzer_TwoFingerSpreadIsIndependentOfImageAspectAndRotation(float aspect, float rotation)
        {
            var points = CreateTwoFinger();
            var angle = 25f * Mathf.Deg2Rad;
            var indexDirection = new Vector3(-Mathf.Sin(angle), -Mathf.Cos(angle), 0f) * 0.15f;
            var middleDirection = new Vector3(0f, -0.15f, 0f);
            points[6] = points[5] + indexDirection * 0.5f;
            points[8] = points[5] + indexDirection;
            points[10] = points[9] + middleDirection * 0.5f;
            points[12] = points[9] + middleDirection;

            var observation = new RgbHandGestureAnalyzer().Analyze(ImageFrame(points, aspect, rotation));

            Assert.That(observation.Gesture, Is.EqualTo(RgbHandGesture.TwoFinger));
        }

        [TestCase(16f / 9f, 0f)]
        [TestCase(16f / 9f, 90f)]
        [TestCase(9f / 16f, 0f)]
        [TestCase(9f / 16f, 90f)]
        public void LandmarkAnalyzer_JointAnglesAreIndependentOfImageAspectAndRotation(float aspect, float rotation)
        {
            var points = CreateOpenHand();
            BendFinger(points, 5, 6, 8, 155f);
            BendFinger(points, 9, 10, 12, 155f);
            BendFinger(points, 13, 14, 16, 155f);
            BendFinger(points, 17, 18, 20, 155f);

            var observation = new RgbHandGestureAnalyzer().Analyze(ImageFrame(points, aspect, rotation));

            Assert.That(observation.Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
        }

        [TestCase(16f / 9f, 0f)]
        [TestCase(16f / 9f, 90f)]
        [TestCase(9f / 16f, 0f)]
        [TestCase(9f / 16f, 90f)]
        public void LandmarkAnalyzer_PinchDistanceUsesWidthUnitsAndPalmKeepsImageCoordinates(float aspect, float rotation)
        {
            var points = CreatePinch(0.04f);
            points[4].z = 0.02f;
            var frame = ImageFrame(points, aspect, rotation);
            var originalPoints = (Vector3[])frame.Landmarks.Clone();

            var observation = new RgbHandGestureAnalyzer().Analyze(frame);

            Assert.That(observation.Gesture, Is.EqualTo(RgbHandGesture.Pinch));
            Assert.That(observation.PeakSeparation, Is.EqualTo(Mathf.Sqrt(0.04f * 0.04f + 0.02f * 0.02f) / 0.20f).Within(0.0001f));
            var palm = (originalPoints[0] + originalPoints[5] + originalPoints[9] + originalPoints[13] + originalPoints[17]) / 5f;
            Assert.That(observation.PalmPosition.x, Is.EqualTo(palm.x).Within(0.0001f));
            Assert.That(observation.PalmPosition.y, Is.EqualTo(palm.y).Within(0.0001f));
            CollectionAssert.AreEqual(originalPoints, frame.Landmarks);
        }

        [Test]
        public void LandmarkAnalyzer_PinchAndExtensionUseHysteresis()
        {
            var analyzer = new RgbHandGestureAnalyzer();
            var entered = analyzer.Analyze(Frame(CreatePinch(0.04f), 0.9f));
            Assert.That(entered.Gesture, Is.EqualTo(RgbHandGesture.Pinch));
            Assert.That(entered.Latch.PinchLatched, Is.True);

            var held = analyzer.Analyze(Frame(CreatePinch(0.08f), 0.9f), entered.Latch);
            Assert.That(held.Gesture, Is.EqualTo(RgbHandGesture.Pinch));
            Assert.That(analyzer.Analyze(Frame(CreatePinch(0.08f), 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.None));

            var released = analyzer.Analyze(Frame(CreatePinch(0.10f), 0.9f), held.Latch);
            Assert.That(released.Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(released.Latch.PinchLatched, Is.False);

            var open = analyzer.Analyze(Frame(CreateOpenHand(), 0.9f));
            var uncertain = CreateOpenHand();
            BendFinger(uncertain, 5, 6, 8, 130f);
            BendFinger(uncertain, 9, 10, 12, 130f);
            BendFinger(uncertain, 13, 14, 16, 130f);
            BendFinger(uncertain, 17, 18, 20, 130f);
            Assert.That(analyzer.Analyze(Frame(uncertain, 0.9f)).Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(analyzer.Analyze(Frame(uncertain, 0.9f), open.Latch).Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
        }

        [Test]
        public void LandmarkAnalyzer_RejectsLowConfidenceAndMissingHand()
        {
            var points = CreateOpenHand();
            points[4] = points[8];
            var analyzer = new RgbHandGestureAnalyzer();

            Assert.That(analyzer.Analyze(Frame(points, 0.2f)).Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(analyzer.Analyze(Frame(points, 0.2f)).HandDetected, Is.False);
            Assert.That(analyzer.Analyze(RgbHandLandmarkFrame.NotTracked(9, 1.0)).Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(analyzer.Analyze(RgbHandLandmarkFrame.NotTracked(9, 1.0)).HandDetected, Is.False);
        }

        [Test]
        public void Stabilizer_ConfirmsAfter250Ms_AndClearsImmediatelyWhenConfidenceDrops()
        {
            Assert.That(RgbHandGestureStabilizer.ConfirmSeconds, Is.EqualTo(0.25d));
            var analyzer = new RgbHandGestureAnalyzer();
            var stabilizer = new RgbHandGestureStabilizer();
            var open = Frame(CreateOpenHand(), 0.9f);

            Assert.That(stabilizer.Observe(analyzer, open, 0d, out var raw), Is.False);
            Assert.That(raw.Gesture, Is.EqualTo(RgbHandGesture.OpenPalm));
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
            Assert.That(stabilizer.Observe(analyzer, open, 0.125d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 0.249d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 0.25d, out _), Is.True);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.OpenPalm));
            Assert.That(stabilizer.Observe(analyzer, open, 0.30d, out _), Is.False);

            var low = new RgbHandLandmarkFrame(CreateOpenHand(), 0.2f, true, 2, 0.31d);
            Assert.That(stabilizer.Observe(analyzer, low, 0.31d, out var lowObservation), Is.True);
            Assert.That(lowObservation.Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(lowObservation.HandDetected, Is.False);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
        }

        [Test]
        public void Stabilizer_KeepsImmediateOkForSliceWhilePublishedGestureWaits()
        {
            var points = CreateOpenHand();
            points[4] = new Vector3(0.47f, 0.30f, 0f);
            points[8] = new Vector3(0.48f, 0.30f, 0f);
            var stabilizer = new RgbHandGestureStabilizer();

            var changed = stabilizer.Observe(new RgbHandGestureAnalyzer(), Frame(points, 0.9f), 0d, out var raw);

            Assert.That(changed, Is.False);
            Assert.That(raw.Gesture, Is.EqualTo(RgbHandGesture.Ok));
            Assert.That(raw.HandDetected, Is.True);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
        }

        [Test]
        public void Stabilizer_HoldsPinchUntilTheExitThresholdThenWaitsToClear()
        {
            var analyzer = new RgbHandGestureAnalyzer();
            var stabilizer = new RgbHandGestureStabilizer();
            var pinch = Frame(CreatePinch(0.04f), 0.9f);
            stabilizer.Observe(analyzer, pinch, 0d, out _);
            Assert.That(stabilizer.Observe(analyzer, pinch, 0.125d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, pinch, 0.25d, out _), Is.True);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.Pinch));

            Assert.That(stabilizer.Observe(analyzer, Frame(CreatePinch(0.08f), 0.9f), 0.30d, out var raw), Is.False);
            Assert.That(raw.Gesture, Is.EqualTo(RgbHandGesture.None));
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.Pinch));

            var released = Frame(CreatePinch(0.10f), 0.9f);
            Assert.That(stabilizer.Observe(analyzer, released, 0.40d, out _), Is.False);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.Pinch));
            Assert.That(stabilizer.Observe(analyzer, released, 0.525d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, released, 0.65d, out _), Is.True);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Stabilizer_FrameGapRestartsPendingConfirmation(bool expireBetweenFrames)
        {
            var analyzer = new RgbHandGestureAnalyzer();
            var stabilizer = new RgbHandGestureStabilizer();
            var open = Frame(CreateOpenHand(), 0.9f);
            stabilizer.Observe(analyzer, open, 0d, out _);

            // Cover both regular Update calls during camera loss and a stalled main thread.
            if (expireBetweenFrames)
                Assert.That(stabilizer.Expire(0.181d), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 3d, out _), Is.False);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
            Assert.That(stabilizer.Observe(analyzer, open, 3.125d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 3.249d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 3.25d, out _), Is.True);
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.OpenPalm));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Stabilizer_FrameGapClearsFingerAndPinchLatches(bool pinch)
        {
            var analyzer = new RgbHandGestureAnalyzer();
            var stabilizer = new RgbHandGestureStabilizer();
            var entered = pinch ? CreatePinch(0.04f) : CreateOpenHand();
            var uncertain = pinch ? CreatePinch(0.08f) : CreateOpenHand();
            if (!pinch)
            {
                BendFinger(uncertain, 5, 6, 8, 130f);
                BendFinger(uncertain, 9, 10, 12, 130f);
                BendFinger(uncertain, 13, 14, 16, 130f);
                BendFinger(uncertain, 17, 18, 20, 130f);
            }
            stabilizer.Observe(analyzer, Frame(entered, 0.9f), 0d, out _);

            foreach (var time in new[] { 3d, 3.125d, 3.25d })
            {
                Assert.That(stabilizer.Observe(analyzer, Frame(uncertain, 0.9f), time, out _), Is.False);
                Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Stabilizer_FrameGapClearsPublishedGestureAndRequiresFreshConfirmation(bool expireBetweenFrames)
        {
            var analyzer = new RgbHandGestureAnalyzer();
            var stabilizer = new RgbHandGestureStabilizer();
            var open = Frame(CreateOpenHand(), 0.9f);
            stabilizer.Observe(analyzer, open, 0d, out _);
            stabilizer.Observe(analyzer, open, 0.125d, out _);
            Assert.That(stabilizer.Observe(analyzer, open, 0.25d, out _), Is.True);

            if (expireBetweenFrames)
            {
                Assert.That(stabilizer.Expire(0.42d), Is.False);
                Assert.That(stabilizer.Expire(0.431d), Is.True);
                Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
                Assert.That(stabilizer.Expire(0.5d), Is.False);
            }
            Assert.That(stabilizer.Observe(analyzer, open, 3d, out _), Is.EqualTo(!expireBetweenFrames));
            Assert.That(stabilizer.Published, Is.EqualTo(RgbHandGesture.None));
            Assert.That(stabilizer.Observe(analyzer, open, 3.125d, out _), Is.False);
            Assert.That(stabilizer.Observe(analyzer, open, 3.25d, out _), Is.True);
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

        static RgbHandLandmarkFrame Frame(Vector3[] points, float confidence)
        {
            return new RgbHandLandmarkFrame(points, confidence, true, 1, 0d);
        }

        static RgbHandLandmarkFrame ImageFrame(Vector3[] widthScaledPoints, float aspect, float rotationDegrees)
        {
            var normalized = new Vector3[widthScaledPoints.Length];
            var radians = rotationDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            for (var i = 0; i < normalized.Length; i++)
            {
                // Rotate the same physical pose before encoding normalized image coordinates.
                // Uniform shrinking keeps all landmarks within either image orientation.
                var x = (widthScaledPoints[i].x - 0.5f) * 0.6f;
                var y = (widthScaledPoints[i].y - 0.5f) * 0.6f;
                normalized[i] = new Vector3(0.5f + x * cos - y * sin,
                    0.5f + (x * sin + y * cos) * aspect, widthScaledPoints[i].z * 0.6f);
            }
            return new RgbHandLandmarkFrame(normalized, 0.9f, true, 1, 0d, aspect);
        }

        static Vector3[] CreatePinch(float tipSeparation)
        {
            var points = CreateOpenHand();
            BendFinger(points, 5, 6, 8, 40f);
            BendFinger(points, 9, 10, 12, 0f);
            BendFinger(points, 13, 14, 16, 0f);
            BendFinger(points, 17, 18, 20, 0f);
            points[4] = points[8] + new Vector3(tipSeparation, 0f, 0f);
            return points;
        }

        static Vector3[] CreateTwoFinger()
        {
            var points = CreateOpenHand();
            points[4] = new Vector3(0.30f, 0.70f, 0f);
            points[5] = new Vector3(0.42f, 0.62f, 0f);
            points[6] = new Vector3(0.32f, 0.50f, 0f);
            points[8] = new Vector3(0.22f, 0.38f, 0f);
            BendFinger(points, 13, 14, 16, 0f);
            BendFinger(points, 17, 18, 20, 0f);
            return points;
        }

        static Vector3[] CreateSpreadOpenPalm()
        {
            var points = CreateOpenHand();
            points[4] = new Vector3(0.30f, 0.70f, 0f);
            points[5] = new Vector3(0.42f, 0.62f, 0f);
            points[6] = new Vector3(0.32f, 0.50f, 0f);
            points[8] = new Vector3(0.22f, 0.38f, 0f);
            return points;
        }

        static void BendFinger(Vector3[] points, int mcp, int pip, int tip, float interiorDegrees)
        {
            var pipPoint = points[mcp] + new Vector3(0f, -0.12f, 0f);
            var offsetFromStraight = (180f - interiorDegrees) * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(offsetFromStraight), -Mathf.Cos(offsetFromStraight), 0f);
            points[pip] = pipPoint;
            points[tip] = pipPoint + direction * 0.12f;
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
