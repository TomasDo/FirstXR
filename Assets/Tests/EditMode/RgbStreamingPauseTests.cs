using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class RgbStreamingPauseTests
    {
        [Test]
        public void SerializedRgbStartupAndDirectToggleDoNotCreateCameraService()
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);
            var host = new GameObject("RGB streaming pause test");
            host.SetActive(false);
            try
            {
                var streamer = host.AddComponent<XrRgbRtpStreamer>();
                SetField(streamer, "m_IncludeRgbOnStart", true);
                host.SetActive(true);
                InvokeStart(streamer);

                Assert.That(streamer.IncludeRgb, Is.False);
                Assert.That(GetField<object>(streamer, "m_CameraService"), Is.Null);

                streamer.SetRgbViewEnabled(true);

                Assert.That(streamer.IncludeRgb, Is.False);
                Assert.That(streamer.Status, Is.EqualTo(RgbFeaturePolicy.DisabledMessage));
                Assert.That(GetField<object>(streamer, "m_CameraService"), Is.Null);
                Assert.That(GetField<bool>(streamer, "m_HasRgbLease"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemoteRgbControlIsRejectedBeforeChangingStreamConfiguration(bool mirrorEnabled)
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);
            var host = new GameObject("RGB remote control pause test");
            try
            {
                var streamer = host.AddComponent<XrRgbRtpStreamer>();
                Assert.That(streamer.ConfigureDestination("192.0.2.10", 5555), Is.True);
                var previousUri = streamer.DestinationUri;
                var previousIncludeXr = streamer.IncludeXr;
                var previousWidth = GetField<int>(streamer, "m_OutputWidth");

                var control = new DentalObservationControlState(
                    "session", 1, 2, mirrorEnabled, true, "192.0.2.99", 6000, 640, 360, 20);

                Assert.That(streamer.TryApplyControl(control, out var error), Is.False);
                Assert.That(error, Is.EqualTo(RgbFeaturePolicy.DisabledMessage));
                Assert.That(streamer.DestinationUri, Is.EqualTo(previousUri));
                Assert.That(streamer.IncludeXr, Is.EqualTo(previousIncludeXr));
                Assert.That(streamer.IncludeRgb, Is.False);
                Assert.That(GetField<int>(streamer, "m_OutputWidth"), Is.EqualTo(previousWidth));
                Assert.That(GetField<object>(streamer, "m_CameraService"), Is.Null);
                Assert.That(streamer.IsStreaming, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RemoteStopStillWorksWhileRgbIsPaused()
        {
            var host = new GameObject("RGB remote stop test");
            try
            {
                var streamer = host.AddComponent<XrRgbRtpStreamer>();
                var control = new DentalObservationControlState(
                    "session", 1, 3, false, false, string.Empty, 0, 0, 0, 0);

                Assert.That(streamer.TryApplyControl(control, out var error), Is.True);
                Assert.That(error, Is.Empty);
                Assert.That(streamer.IncludeXr, Is.False);
                Assert.That(streamer.IncludeRgb, Is.False);
                Assert.That(streamer.IsStreaming, Is.False);
                Assert.That(GetField<object>(streamer, "m_CameraService"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void XrOnlyRequestWithoutLiveLeftEyeFrameDoesNotStartEncoder()
        {
            var host = new GameObject("XR-only unavailable source test");
            try
            {
                var streamer = host.AddComponent<XrRgbRtpStreamer>();
                var control = new DentalObservationControlState(
                    "session", 1, 4, true, false, "192.0.2.10", 5555, 640, 360, 15);

                Assert.That(streamer.TryApplyControl(control, out var error), Is.False);
                Assert.That(error, Does.Contain("XR left-eye output is not currently available"));
                Assert.That(streamer.IsStreaming, Is.False);
                Assert.That(streamer.IncludeRgb, Is.False);
                Assert.That(GetField<object>(streamer, "m_CameraService"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        static void InvokeStart(XrRgbRtpStreamer streamer)
        {
            var method = typeof(XrRgbRtpStreamer).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(streamer, null);
        }

        static void SetField(XrRgbRtpStreamer streamer, string name, object value)
        {
            var field = typeof(XrRgbRtpStreamer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(streamer, value);
        }

        static T GetField<T>(XrRgbRtpStreamer streamer, string name)
        {
            var field = typeof(XrRgbRtpStreamer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(streamer);
        }
    }
}
