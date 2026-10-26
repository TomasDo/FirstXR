using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class RgbFeaturePauseUiTests
    {
        [Test]
        public void DisabledProviderFactory_IsNeverInvoked()
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);
            var factoryCalls = 0;
            RgbHandLandmarkProviderRegistry.RegisterFactory(() =>
            {
                factoryCalls++;
                return new ScriptedRgbHandLandmarkProvider();
            });
            try
            {
                using (var provider = RgbHandLandmarkProviderRegistry.CreateProvider())
                {
                    Assert.That(provider.IsAvailable, Is.False);
                    Assert.That(provider.Status, Is.EqualTo(RgbFeaturePolicy.DisabledMessage));
                }
                Assert.That(factoryCalls, Is.Zero);
            }
            finally
            {
                RgbHandLandmarkProviderRegistry.ClearFactory();
            }
        }

        [Test]
        public void PreviewDirectCalls_DoNotCreateCameraOrPreviewResources()
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);
            var originalCamera = RgbCameraFrameService.Instance;
            var host = new GameObject("Paused RGB preview test");
            try
            {
                var preview = host.AddComponent<RGBCameraFloatingWindow>();
                preview.StartCapture();
                preview.SetWindowVisible(true);

                Assert.That(preview.IsWindowVisible, Is.False);
                Assert.That(preview.IsCapturing, Is.False);
                Assert.That(preview.TryGetYuvTextures(out var y, out var u, out var v), Is.False);
                Assert.That(y, Is.Null);
                Assert.That(u, Is.Null);
                Assert.That(v, Is.Null);
                Assert.That(preview.CreateYuvMaterialInstance(), Is.Null);
                Assert.That(RgbCameraFrameService.Instance, Is.SameAs(originalCamera));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GestureDirectCalls_DoNotCreateProviderOrCamera()
        {
            Assert.That(RgbFeaturePolicy.Enabled, Is.False);
            var originalCamera = RgbCameraFrameService.Instance;
            var host = new GameObject("Paused RGB gesture test");
            try
            {
                var recognizer = host.AddComponent<RgbHandGestureRecognizer>();
                recognizer.SetRecognitionEnabled(true);
                recognizer.ToggleRecognitionEnabled();
                recognizer.RefreshProvider();

                Assert.That(recognizer.RecognitionEnabled, Is.False);
                Assert.That(recognizer.ProviderAvailable, Is.False);
                Assert.That(recognizer.ProviderName, Is.EqualTo("not created"));
                Assert.That(recognizer.CurrentGesture, Is.EqualTo(RgbHandGesture.None));
                Assert.That(RgbCameraFrameService.Instance, Is.SameAs(originalCamera));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
