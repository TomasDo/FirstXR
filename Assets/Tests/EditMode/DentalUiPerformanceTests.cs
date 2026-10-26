using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Unity.XR.XREAL.Samples;
using Object = UnityEngine.Object;

namespace DentalNavigation.Tests
{
    public sealed class DentalUiPerformanceTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        delegate BeamProPageLayoutResult LayoutGetter(BeamProPageLayoutRequest request,
            string asset, string connection, string operation, string logs, string context, string alert);

        [Test]
        public void RepeatedLogReadsReuseTextUntilStatusOrLinesChange()
        {
            var source = "Cache-" + Guid.NewGuid();
            BeamProUnifiedLogWindow.SetStatus(source, "ready");
            var first = BeamProUnifiedLogWindow.SnapshotText;
            Assert.That(BeamProUnifiedLogWindow.SnapshotText, Is.SameAs(first));
            BeamProUnifiedLogWindow.SetStatus(source, new string("ready".ToCharArray()));
            Assert.That(BeamProUnifiedLogWindow.SnapshotText, Is.SameAs(first));

            BeamProUnifiedLogWindow.SetStatus(source, "connected");
            var changed = BeamProUnifiedLogWindow.SnapshotText;
            Assert.That(changed, Is.Not.SameAs(first));
            Assert.That(changed, Does.Contain("connected"));
            BeamProUnifiedLogWindow.AddLine(source, "navigation started");
            var appended = BeamProUnifiedLogWindow.SnapshotText;
            Assert.That(appended, Does.Contain("navigation started"));
            Assert.That(appended, Is.Not.SameAs(changed));
            BeamProUnifiedLogWindow.SetStatus(source, null);
            Assert.That(BeamProUnifiedLogWindow.SnapshotText, Is.Not.SameAs(appended));
        }

        [Test]
        public void CachedLogSnapshotKeepsNewest32LinesAndInvalidatesForEmptyNewSource()
        {
            var source = "Bounded-" + Guid.NewGuid();
            for (var i = 0; i < 35; i++)
                BeamProUnifiedLogWindow.AddLine(source, source + "-" + i.ToString("00"));
            var text = BeamProUnifiedLogWindow.SnapshotText;
            Assert.That(text, Does.Not.Contain(source + "-00"));
            Assert.That(text, Does.Not.Contain(source + "-02"));
            Assert.That(text, Does.Contain(source + "-03"));
            Assert.That(text.IndexOf(source + "-34", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf(source + "-03", StringComparison.Ordinal)));
            var emptySource = "Empty-" + Guid.NewGuid();
            BeamProUnifiedLogWindow.SetStatus(emptySource, string.Empty);
            Assert.That(BeamProUnifiedLogWindow.SnapshotText, Does.Contain("[" + emptySource + "]"));
        }

        [Test]
        public void StableLogReadsAllocateNoManagedMemoryAfterWarmup()
        {
            BeamProUnifiedLogWindow.SetStatus("AllocationTest", "steady");
            for (var i = 0; i < 10; i++) _ = BeamProUnifiedLogWindow.SnapshotText;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var characters = 0;
            for (var i = 0; i < 1000; i++) characters += BeamProUnifiedLogWindow.SnapshotText.Length;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"1000 stable SnapshotText reads: {bytes} managed bytes, {characters} characters.");
            Assert.That(bytes, Is.Zero);
        }

        [Test]
        public void LayoutReusesGeometryAndAllocatesNothingUntilInputsChange()
        {
            WithLayout((get, host) =>
            {
                var request = PortraitRequest();
                var first = get(request, "ready", "", "", "", "context", "");
                for (var i = 0; i < 10; i++) get(request, "ready", "", "", "", "context", "");
                var before = GC.GetAllocatedBytesForCurrentThread();
                BeamProPageLayoutResult repeated = null;
                for (var i = 0; i < 1000; i++)
                    repeated = get(request, "ready", "", "", "", "context", "");
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(repeated, Is.SameAs(first));
                Assert.That(bytes, Is.Zero);
                TestContext.WriteLine($"1000 stable GetLayout calls: {bytes} managed bytes.");

                request.KeyboardInsetPixels = 300;
                var keyboard = get(request, "ready", "", "", "", "context", "");
                Assert.That(keyboard, Is.Not.SameAs(first));
                Assert.That(keyboard.ContentViewport.height, Is.LessThan(first.ContentViewport.height));
                request.ScreenSizePixels = new Vector2(1920, 1080);
                request.SafeAreaPixels = new Rect(40, 0, 1840, 1080);
                var landscape = get(request, "ready", "", "", "", "context", "");
                Assert.That(landscape, Is.Not.SameAs(keyboard));
                Assert.That(landscape.Orientation, Is.EqualTo(BeamProPageOrientation.Landscape));
                request.SelectedPage = BeamProPage.Connection;
                Assert.That(get(request, "", "ready", "", "", "context", "").SelectedPage,
                    Is.EqualTo(BeamProPage.Connection));
            });
        }

        [Test]
        public void LayoutInvalidatesForWrappedTextAlertsAndEngineerControls()
        {
            WithLayout((get, host) =>
            {
                var request = PortraitRequest();
                var first = get(request, "ready", "", "", "", "context", "");
                var longText = string.Join("\n", new string[40]).Replace("\n", "status\n");
                var expanded = get(request, longText, "", "", "", "context", "alarm");
                Assert.That(expanded.Monitor.AssetStatus.height, Is.GreaterThan(first.Monitor.AssetStatus.height));
                var alertOnly = get(request, longText, "", "", "", "context", longText);
                Assert.That(alertOnly.TopStatusBar.height, Is.GreaterThan(expanded.TopStatusBar.height));
                request.EngineerMode = true;
                request.SelectedPage = BeamProPage.Debug;
                var debug = get(request, "", "", "", longText, "context", "");
                Assert.That(debug.TabButtons.Length, Is.EqualTo(3));
                request.DebugShowObjectMoveControls = false;
                var hiddenControls = get(request, "", "", "", longText, "context", "");
                Assert.That(hiddenControls, Is.Not.SameAs(debug));
                Assert.That(hiddenControls.Debug.ControlRows[6].height, Is.Zero);
            });
        }

        [Test]
        public void StableEndpointAndTransferPropertiesReuseStringsAndReflectChanges()
        {
            var host = new GameObject("UI display cache test");
            host.SetActive(false);
            try
            {
                var display = host.AddComponent<DentalRobotBeamProDisplay>();
                display.ConfigureEndpoint("127.0.0.1", 50051, "device", "dataset");
                var endpoint = display.ServerAddress;
                Assert.That(display.ServerAddress, Is.SameAs(endpoint));
                display.ConfigureEndpoint("127.0.0.2", 50052, "device", "dataset");
                Assert.That(display.ServerAddress, Is.EqualTo("127.0.0.2:50052"));
                display.ApplyTransferEnd("dataset", false, "failed", 123, 456);
                var transfer = display.TransferSummary;
                Assert.That(display.TransferSummary, Is.SameAs(transfer));
                display.ApplyTransferEnd("dataset", false, "retry failed", 987, 654);
                Assert.That(display.TransferSummary, Does.Contain("987 B"));
                Assert.That(display.TransferSummary, Is.Not.SameAs(transfer));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void HudUpdateKeepsFreshValuesAndRefreshesCtThenShowsStaleAndDisconnectedState()
        {
            var stateHost = new GameObject("HUD cache state test");
            var hudHost = new GameObject("HUD update integration test");
            var originalCtService = DentalCtVolumeService.Instance;
            DentalHudController hud = null;
            try
            {
                var state = stateHost.AddComponent<DentalNavigationState>();
                Assert.That(state.ApplyNavigationContext(new DentalNavigationContext(
                    "cache-session", "case", "dataset", "ct", "plan", "36", "drill", "step", 1,
                    Vector3.zero, Vector3.forward, 10, Vector3.up, Vector3.left,
                    "DICOM_PATIENT_LPS", true, Matrix4x4.identity)), Is.True);
                hud = hudHost.AddComponent<DentalHudController>();
                // EditMode does not invoke ordinary MonoBehaviour Awake automatically. Invoke it
                // on the active host so TMP's ExecuteAlways components initialize their materials.
                typeof(DentalHudController).GetMethod("Awake", PrivateInstance).Invoke(hud, null);
                var ct = new TestCtSource();
                SetHudField(hud, "m_CtSource", ct);
                var update = (Action)typeof(DentalHudController).GetMethod("Update", PrivateInstance)
                    .CreateDelegate(typeof(Action), hud);
                Assert.That(state.ApplyNavigationFrame(Frame(1, 0.1f, 4f)), Is.True);
                update();
                var depth = HudLabel(hud, "DepthValue");
                var lateral = HudLabel(hud, "LateralRead");
                var originalText = lateral.text;
                Assert.That(depth.text, Is.EqualTo("4.0"));
                update();
                Assert.That(lateral.text, Is.SameAs(originalText), "Unchanged frames should reuse the formatted text.");

                ct.SliceIndex = 2;
                update();
                Assert.That(HudLabel(hud, "CtStatus").text, Does.Contain("3/10"),
                    "A slice change must update independently of the navigation-frame cache.");
                typeof(DentalNavigationState).GetField("m_SourceAgeSecondsAtArrival", PrivateInstance)
                    .SetValue(state, 0.3f);
                update();
                Assert.That(hud.LastEvaluation.AlarmText, Is.EqualTo("Navigation data delayed"));
                Assert.That(HudLabel(hud, "AlarmText").text, Is.EqualTo("Navigation data delayed"));
                typeof(DentalNavigationState).GetField("m_SourceAgeSecondsAtArrival", PrivateInstance)
                    .SetValue(state, 0.8f);
                update();
                Assert.That(depth.text, Is.EqualTo("—"));
                Assert.That(lateral.text, Is.EqualTo("Position\n—"));

                Assert.That(state.ApplyNavigationFrame(Frame(2, 0.8f, 2f)), Is.True);
                update();
                Assert.That(depth.text, Is.EqualTo("2.0"));
                Assert.That(lateral.text, Does.Contain("0.8"));
                hud.SetHudVisible(false);
                state.NotifyDisconnected("connection lost");
                update();
                Assert.That(hud.LastEvaluation.DashNumbers, Is.True,
                    "Hidden HUDs must continue evaluating navigation safety state.");
                hud.SetHudVisible(true);
                update();
                Assert.That(depth.text, Is.EqualTo("—"));
                Assert.That(HudLabel(hud, "AlarmText").text, Is.EqualTo("Not connected to navigation software"));
            }
            finally
            {
                if (hud != null)
                {
                    var root = (Transform)typeof(DentalHudController).GetField("m_HudRoot", PrivateInstance).GetValue(hud);
                    SetHudField(hud, "m_HudRoot", null);
                    if (root != null) Object.DestroyImmediate(root.gameObject);
                    foreach (var field in new[] { "m_CircleSprite", "m_RingSprite", "m_WhiteSprite", "m_TriangleSprite" })
                    {
                        var sprite = (Sprite)typeof(DentalHudController).GetField(field, PrivateInstance).GetValue(hud);
                        SetHudField(hud, field, null);
                        if (sprite == null) continue;
                        if (sprite.texture != null) Object.DestroyImmediate(sprite.texture);
                        Object.DestroyImmediate(sprite);
                    }
                }
                Object.DestroyImmediate(hudHost);
                Object.DestroyImmediate(stateHost);
                if (originalCtService == null && DentalCtVolumeService.Instance != null)
                    Object.DestroyImmediate(DentalCtVolumeService.Instance.gameObject);
            }
        }

        static void SetHudField(DentalHudController hud, string name, object value)
        {
            typeof(DentalHudController).GetField(name, PrivateInstance).SetValue(hud, value);
        }

        static TMP_Text HudLabel(DentalHudController hud, string name)
        {
            var views = typeof(DentalHudController).GetField("m_Views", PrivateInstance).GetValue(hud);
            return (TMP_Text)views.GetType().GetField(name).GetValue(views);
        }

        sealed class TestCtSource : IDentalCtSliceSource
        {
            public bool HasVolume => true;
            public bool UsesPatientPlane => false;
            public int SliceIndex { get; set; }
            public int SliceCount => 10;
            public float SliceOffsetMm { get; private set; }
            public Texture2D CurrentSliceTexture => null;
            public string StatusMessage => "ready";
            public event Action SliceChanged { add { } remove { } }
            public void SetSliceIndex(int index) { SliceIndex = index; }
            public void StepSlice(int delta) { SliceIndex += delta; }
            public void SetSliceOffsetMm(float offset) { SliceOffsetMm = offset; }
        }

        static DentalNavigationFrameData Frame(ulong sequence, float lateral, float remaining)
        {
            return new DentalNavigationFrameData("cache-session", 1, sequence,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, string.Empty,
                Vector3.zero, Vector3.forward, true, Matrix4x4.identity,
                lateral, lateral, 0, 1, 1, 0, 10 - remaining, 10, remaining, true, true, true);
        }

        static BeamProPageLayoutRequest PortraitRequest()
        {
            return new BeamProPageLayoutRequest(new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920));
        }

        static void WithLayout(Action<LayoutGetter, GameObject> test)
        {
            var host = new GameObject("UI layout cache test");
            host.SetActive(false);
            try
            {
                var controller = host.AddComponent<BeamProPagedController>();
                foreach (var field in new[] { "m_BodyStyle", "m_HeaderContextStyle", "m_HeaderAlarmStyle" })
                    typeof(BeamProPagedController).GetField(field, PrivateInstance).SetValue(controller,
                        new GUIStyle { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), fontSize = 20, wordWrap = true });
                var get = (LayoutGetter)typeof(BeamProPagedController).GetMethod("GetLayout", PrivateInstance)
                    .CreateDelegate(typeof(LayoutGetter), controller);
                test(get, host);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
