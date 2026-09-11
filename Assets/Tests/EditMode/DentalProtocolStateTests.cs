using System;
using System.Reflection;
using Dentalmodeltransfer;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalProtocolStateTests
    {
        GameObject m_Host;
        DentalNavigationState m_State;

        [SetUp]
        public void SetUp()
        {
            m_Host = new GameObject("DentalProtocolStateTests");
            m_State = m_Host.AddComponent<DentalNavigationState>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Host != null)
                UnityEngine.Object.DestroyImmediate(m_Host);
        }

        [Test]
        public void V1WireFieldNumbersRemainStableAndV2UsesDistinctEndSignals()
        {
            Assert.That(ClientMessage.RequestFieldNumber, Is.EqualTo(1));
            Assert.That(ClientMessage.AckFieldNumber, Is.EqualTo(2));
            Assert.That(ServerMessage.MetadataFieldNumber, Is.EqualTo(1));
            Assert.That(ServerMessage.StlChunkFieldNumber, Is.EqualTo(2));
            Assert.That(ServerMessage.EndFieldNumber, Is.EqualTo(3));
            Assert.That(ModelMetadata.DatasetIdFieldNumber, Is.EqualTo(1));
            Assert.That(ModelMetadata.DrillFromTeethFieldNumber, Is.EqualTo(2));
            Assert.That(ModelMetadata.DistanceFieldNumber, Is.EqualTo(3));
            Assert.That(ModelMetadata.LateralDistanceFieldNumber, Is.EqualTo(4));
            Assert.That(ModelMetadata.AngleFieldNumber, Is.EqualTo(5));
            Assert.That(TransferEnd.TeethBytesFieldNumber, Is.EqualTo(4));
            Assert.That(TransferEnd.DrillBytesFieldNumber, Is.EqualTo(5));

            Assert.That(ServerMessage.SessionEndFieldNumber, Is.Not.EqualTo(ServerMessage.EndFieldNumber));
            Assert.That(AssetServerMessage.CompleteFieldNumber, Is.Not.EqualTo(AssetServerMessage.SessionEndFieldNumber));
            Assert.That(SlicePlane.HasPhysicalPlaneFieldNumber, Is.EqualTo(9));
        }

        [Test]
        public void ProtoSlicePlaneExplicitlyDistinguishesNativeIndexFromPhysicalPlane()
        {
            var converter = typeof(DentalRobotGrpcClient).GetMethod(
                "ToDomain",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(SliceState) },
                null);
            Assert.That(converter, Is.Not.Null);

            var native = (DentalSliceState)converter.Invoke(null, new object[]
            {
                new SliceState
                {
                    SessionId = "session",
                    ContextVersion = 1,
                    ControlVersion = 2,
                    Plane = new SlicePlane { SliceIndex = 7, HasPhysicalPlane = false },
                }
            });
            Assert.That(native.HasPlane, Is.False);
            Assert.That(native.SliceIndex, Is.EqualTo(7));

            var physical = (DentalSliceState)converter.Invoke(null, new object[]
            {
                new SliceState
                {
                    SessionId = "session",
                    ContextVersion = 1,
                    ControlVersion = 3,
                    Plane = new SlicePlane
                    {
                        HasPhysicalPlane = true,
                        OriginMm = new Vector3d { X = 1, Y = 2, Z = 3 },
                        Normal = new Vector3d { Z = 1 },
                        Up = new Vector3d { Y = 1 },
                    },
                }
            });
            Assert.That(physical.HasPlane, Is.True);
            Assert.That(physical.PlaneOriginMm, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void ContextVersionAndFrameSequenceRejectStaleData()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(2)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(2, 10, 1.2f)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(2, 9, 8.8f)), Is.False);

            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.FrameSequence, Is.EqualTo(10));
            Assert.That(snapshot.LateralMm, Is.EqualTo(1.2f).Within(0.001f));

            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.False);
            Assert.That(m_State.ApplyNavigationContext(Context(3)), Is.True);
            snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.ContextVersion, Is.EqualTo(3));
            Assert.That(snapshot.HasNavigationFrame, Is.False);
            Assert.That(snapshot.HasThresholds, Is.False);
        }

        [Test]
        public void InvalidFrameKeepsReasonButHidesDynamicValues()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var invalid = new DentalNavigationFrameData(
                "session", 1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false, "tracking lost",
                Vector3.zero, Vector3.forward, false, Matrix4x4.identity,
                0, 0, 0, 0, 0, 0, 0, 10, 10,
                true, true, true);

            Assert.That(m_State.ApplyNavigationFrame(invalid), Is.True);
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.HasMetadata, Is.True);
            Assert.That(snapshot.HasNavigationFrame, Is.True);
            Assert.That(snapshot.FrameValid, Is.False);
            Assert.That(snapshot.InvalidReason, Is.EqualTo("tracking lost"));
            Assert.That(snapshot.HideNumbers, Is.True);
            Assert.That(snapshot.HasDrillMatrix, Is.False);
        }

        [Test]
        public void ThresholdsMustMatchContextAndAreIdempotentByVersion()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(4)), Is.True);
            var wrongContext = Thresholds(3, 1);
            Assert.That(m_State.ApplyThresholds(wrongContext), Is.False);
            Assert.That(m_State.Capture(Time.realtimeSinceStartup).HasThresholds, Is.False);

            var current = Thresholds(4, 2);
            Assert.That(m_State.ApplyThresholds(current), Is.True);
            Assert.That(m_State.ApplyThresholds(current), Is.True);
            Assert.That(m_State.ApplyThresholds(Thresholds(4, 1)), Is.False);
            Assert.That(m_State.Capture(Time.realtimeSinceStartup).Thresholds.ConfigVersion, Is.EqualTo(2));
        }

        [Test]
        public void OldSessionUnitErrorCannotClearCurrentThresholds()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(4)), Is.True);
            Assert.That(m_State.ApplyThresholds(Thresholds(4, 2)), Is.True);

            m_State.NotifyThresholdsUnavailable("old-session", 4, 3, "bad units");

            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.HasThresholds, Is.True);
            Assert.That(snapshot.Thresholds.ConfigVersion, Is.EqualTo(2));
        }

        [Test]
        public void ThresholdsRejectZeroVersionAndLockingHysteresis()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            Assert.That(m_State.ApplyThresholds(Thresholds(1, 0)), Is.False);
            var locking = new DentalNavigationThresholds(
                "session", 1, 1,
                0.5f, 1f, 0.6f,
                2f, 5f, 0.4f,
                1f, 0.1f, 0.2f, 0.1f,
                DentalThresholdBoundaryRule.UpperBoundsInclusive);
            Assert.That(m_State.ApplyThresholds(locking), Is.False);
        }

        [Test]
        public void DirectionComponentsMustMatchAuthoritativeMagnitude()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var mismatched = new DentalNavigationFrameData(
                "session", 1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                Vector3.zero, Vector3.forward, false, Matrix4x4.identity,
                1f, 0f, 0f,
                2f, 0f, 0f,
                4f, 10f, 6f,
                true, true, true);

            Assert.That(m_State.ApplyNavigationFrame(mismatched), Is.True);
            Assert.That(m_State.Capture(Time.realtimeSinceStartup).FrameValid, Is.False);
        }

        [Test]
        public void DynamicNumbersHideAfterFiveHundredMilliseconds()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 1, 0.3f)), Is.True);
            var delayed = m_State.Capture(Time.realtimeSinceStartup + 0.51f);
            Assert.That(delayed.IsStale, Is.True);
            Assert.That(delayed.HideNumbers, Is.True);
        }

        [Test]
        public void SourceCaptureTimestampContributesToFrameAge()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var oldFrame = Frame(1, 1, 0.3f, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 600);
            Assert.That(m_State.ApplyNavigationFrame(oldFrame), Is.True);

            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.AgeSeconds, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(snapshot.HideNumbers, Is.True);
        }

        [Test]
        public void InvalidUnixTimestampCannotOverflowIntoAFreshFrame()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            Assert.That(m_State.ApplyNavigationFrame(Frame(1, 1, 0.3f, long.MinValue)), Is.True);

            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.FrameValid, Is.False);
            Assert.That(snapshot.InvalidReason, Does.Contain("采集时间"));
        }

        [Test]
        public void FrameTargetDepthMustMatchCurrentStepContext()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var mismatched = new DentalNavigationFrameData(
                "session", 1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                Vector3.zero, Vector3.forward, false, Matrix4x4.identity,
                0f, 0f, 0f,
                0f, 0f, 0f,
                4f, 12f, 8f,
                true, true, true);

            Assert.That(m_State.ApplyNavigationFrame(mismatched), Is.True);
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.FrameValid, Is.False);
            Assert.That(snapshot.InvalidReason, Does.Contain("目标深度"));
        }

        [Test]
        public void ContextRejectsMalformedOrScaledCoordinateTransforms()
        {
            var scaled = Matrix4x4.Scale(new Vector3(2f, 1f, 1f));
            var context = new DentalNavigationContext(
                "session", "case", "dataset", "ct", "plan", "36", "drill", "step", 1,
                Vector3.zero, Vector3.forward, 10f, Vector3.up, Vector3.left,
                "patient", true, scaled);
            Assert.That(m_State.ApplyNavigationContext(context), Is.False);

            var converter = typeof(DentalNavigationState).GetMethod(
                "ToUnityMatrix",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(System.Collections.Generic.IList<double>) },
                null);
            Assert.That(converter, Is.Not.Null);
            var values = new System.Collections.Generic.List<double>();
            for (var index = 0; index < 17; index++)
                values.Add(index % 5 == 0 ? 1d : 0d);
            var malformedMatrix = (Matrix4x4)converter.Invoke(null, new object[] { values });
            var malformed = new DentalNavigationContext(
                "session", "case", "dataset", "ct", "plan", "36", "drill", "step", 1,
                Vector3.zero, Vector3.forward, 10f, Vector3.up, Vector3.left,
                "patient", true, malformedMatrix);
            Assert.That(m_State.ApplyNavigationContext(malformed), Is.False);
        }

        [Test]
        public void InvalidDrillTransformMakesFrameInvalid()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var matrix = Matrix4x4.identity;
            matrix[0, 0] = float.NaN;
            var frame = new DentalNavigationFrameData(
                "session", 1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                Vector3.zero, Vector3.forward, true, matrix,
                0f, 0f, 0f, 0f, 0f, 0f, 4f, 10f, 6f,
                true, true, true);

            Assert.That(m_State.ApplyNavigationFrame(frame), Is.True);
            Assert.That(m_State.Capture(Time.realtimeSinceStartup).FrameValid, Is.False);
        }

        [Test]
        public void SliceStateRejectsUnspecifiedSourceAndInvalidPhysicalPlane()
        {
            Assert.That(m_State.ApplyNavigationContext(Context(1)), Is.True);
            var unspecified = new DentalSliceState(
                "session", 1, 1, true, DentalControlSource.Unspecified,
                false, "ct", string.Empty, default, default, default, 0f, 0, string.Empty);
            Assert.That(m_State.ApplySliceState(unspecified), Is.False);

            var collinear = new DentalSliceState(
                "session", 1, 2, true, DentalControlSource.NavigationSoftware,
                true, "ct", "patient", Vector3.zero, Vector3.forward, Vector3.forward,
                0f, 0, string.Empty);
            Assert.That(m_State.ApplySliceState(collinear), Is.False);

            var negativeIndex = new DentalSliceState(
                "session", 1, 3, true, DentalControlSource.NavigationSoftware,
                false, "ct", string.Empty, default, default, default, 0f, -1, string.Empty);
            Assert.That(m_State.ApplySliceState(negativeIndex), Is.False);
        }

        [Test]
        public void LegacyMetadataDoesNotInventDirectionalOrDepthBreakdownData()
        {
            m_State.ApplyMetadata("legacy", null, 0.004, 0.0005, 2.0);
            var snapshot = m_State.Capture(Time.realtimeSinceStartup);
            Assert.That(snapshot.HasNavigationFrame, Is.True);
            Assert.That(snapshot.FrameValid, Is.True);
            Assert.That(snapshot.HasLateralDirection, Is.False);
            Assert.That(snapshot.HasTiltDirection, Is.False);
            Assert.That(snapshot.HasDepthBreakdown, Is.False);
            Assert.That(snapshot.RemainingDepthMm, Is.EqualTo(4f).Within(0.001f));
        }

        static DentalNavigationContext Context(ulong version)
        {
            return new DentalNavigationContext(
                "session", "case", "dataset", "ct", "plan", "36", "drill", "step",
                version,
                Vector3.zero,
                Vector3.forward,
                10f,
                Vector3.up,
                Vector3.left,
                "patient",
                true,
                Matrix4x4.identity);
        }

        static DentalNavigationFrameData Frame(
            ulong contextVersion,
            ulong sequence,
            float lateralMm,
            long? captureTimeUnixMs = null)
        {
            return new DentalNavigationFrameData(
                "session", contextVersion, sequence,
                captureTimeUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                new Vector3(1, 2, 3), Vector3.forward, true, Matrix4x4.identity,
                lateralMm, lateralMm, 0f, 2f, 2f, 0f, 4f, 10f, 6f,
                true, true, true);
        }

        static DentalNavigationThresholds Thresholds(ulong contextVersion, ulong configVersion)
        {
            return new DentalNavigationThresholds(
                "session", contextVersion, configVersion,
                0.5f, 1f, 0.1f,
                2f, 5f, 0.4f,
                1f, 0.1f, 0.2f, 0.1f,
                DentalThresholdBoundaryRule.UpperBoundsInclusive);
        }
    }
}
