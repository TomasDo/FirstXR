using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class RgbCameraPauseTests
    {
        [Test]
        public void PausedCamera_DoesNotInitializeOrCaptureAcrossStartupResumeAndPlugEvents()
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);

            var nativeCameraBefore = XREALRGBCameraTexture.Singleton;
            var host = new GameObject("RGB pause test");
            try
            {
                var service = host.AddComponent<RgbCameraFrameService>();
                var consumer = new object();

                Assert.That(InvokeIterator(service, "Start").MoveNext(), Is.False);
                Assert.That(InvokeIterator(service, "InitializeAsync").MoveNext(), Is.False);
                Assert.That(InvokeIterator(service, "RequestPermissionIfNeeded").MoveNext(), Is.False);

                service.AcquireCapture(consumer);
                host.SetActive(false);
                host.SetActive(true);
                Invoke(service, "OnPlugStateChanged", XREALRGBCameraPlugState.PLUGOUT);
                Invoke(service, "OnPlugStateChanged", XREALRGBCameraPlugState.PLUGIN);
                Assert.That(InvokeIterator(service, "StartCaptureWithRetry").MoveNext(), Is.False);

                Assert.That(service.ConsumerCount, Is.Zero);
                Assert.That(service.IsReady, Is.False);
                Assert.That(service.IsCapturing, Is.False);
                Assert.That(service.TryGetLatestFrame(out var frame), Is.False);
                Assert.That(frame.IsValid, Is.False);
                Assert.That(XREALRGBCameraTexture.Singleton, Is.EqualTo(nativeCameraBefore));
                Assert.That(GetField<bool>(service, "m_Initializing"), Is.False);
                Assert.That(GetField<bool>(service, "m_SubscribedToFrames"), Is.False);
                Assert.That(GetField<bool>(service, "m_SubscribedToPlugState"), Is.False);
                Assert.That(GetField<object>(service, "m_StartCoroutine"), Is.Null);

                service.ReleaseCapture(consumer);
                Assert.That(service.ConsumerCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        static IEnumerator InvokeIterator(RgbCameraFrameService service, string methodName)
        {
            return (IEnumerator)GetMethod(methodName).Invoke(service, null);
        }

        static void Invoke(RgbCameraFrameService service, string methodName, object argument)
        {
            GetMethod(methodName).Invoke(service, new[] { argument });
        }

        static MethodInfo GetMethod(string methodName)
        {
            var method = typeof(RgbCameraFrameService).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, methodName);
            return method;
        }

        static T GetField<T>(RgbCameraFrameService service, string fieldName)
        {
            var field = typeof(RgbCameraFrameService).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(service);
        }
    }
}
