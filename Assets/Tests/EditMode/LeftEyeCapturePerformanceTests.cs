using System.Reflection;
using NUnit.Framework;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DentalNavigation.Tests
{
    public sealed class LeftEyeCapturePerformanceTests
    {
        static readonly MethodInfo BeginCapture = typeof(LeftEyeDisplayWindow).GetMethod(
            "TryBeginCapture", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo CaptureSize = typeof(LeftEyeDisplayWindow).GetMethod(
            "GetCaptureSize", BindingFlags.Static | BindingFlags.NonPublic);

        GameObject m_Host;
        LeftEyeDisplayWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Host = new GameObject("Left eye capture regression");
            m_Window = m_Host.AddComponent<LeftEyeDisplayWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Host);
        }

        [TestCase(true, false, 30)]
        [TestCase(false, false, 10)]
        [TestCase(true, true, 60)]
        [TestCase(false, true, 60)]
        public void CaptureCadenceKeepsPhoneWorkBoundedWithoutThrottlingStreams(
            bool visible, bool streaming, int expectedCaptures)
        {
            var captures = 0;
            for (var frame = 0; frame < 60; frame++)
                if (TryBegin(frame / 60.0, visible, streaming))
                    captures++;

            Assert.That(captures, Is.EqualTo(expectedCaptures));
        }

        [Test]
        public void ShowingPreviewOrStartingStreamDoesNotWaitForWarmupTimer()
        {
            Assert.That(TryBegin(1.0, false, false), Is.True);
            Assert.That(TryBegin(1.01, false, false), Is.False);
            Assert.That(TryBegin(1.02, true, false), Is.True);
            Assert.That(TryBegin(1.025, true, false), Is.False);
            Assert.That(TryBegin(1.03, true, true), Is.True);
            Assert.That(TryBegin(1.04, false, true), Is.True);
        }

        [Test]
        public void HiddenWarmupStaysInsideLiveFrameFreshnessWindow()
        {
            var lastCapture = -1.0;
            for (var frame = 0; frame < 180; frame++)
            {
                var now = frame / 60.0;
                if (TryBegin(now, false, false))
                    lastCapture = now;
                Assert.That(now - lastCapture, Is.LessThan(0.25),
                    "A hidden phone page must not make a healthy XR stream fail the startup freshness gate.");
            }
        }

        [TestCase(1920, 1080, true, false, 1280, 720)]
        [TestCase(1920, 1080, false, false, 320, 180)]
        [TestCase(1080, 1920, true, false, 405, 720)]
        [TestCase(1080, 1920, false, false, 101, 180)]
        [TestCase(640, 360, true, false, 640, 360)]
        [TestCase(3840, 2160, false, true, 3840, 2160)]
        public void PhoneCapturePreservesAspectAndStreamCaptureKeepsNativeSize(
            int width, int height, bool visible, bool streaming, int expectedWidth, int expectedHeight)
        {
            var size = (Vector2Int)CaptureSize.Invoke(null, new object[] { width, height, visible, streaming });
            Assert.That(size, Is.EqualTo(new Vector2Int(expectedWidth, expectedHeight)));
            Assert.That((float)size.x / size.y, Is.EqualTo((float)width / height).Within(0.01f));
        }

        bool TryBegin(double now, bool visible, bool streaming)
        {
            return (bool)BeginCapture.Invoke(m_Window, new object[] { now, visible, streaming });
        }
    }
}
