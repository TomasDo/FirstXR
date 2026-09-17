using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class BeamProPageLayoutTests
    {
        const float Tolerance = 0.01f;

        [Test]
        public void PortraitReferenceScreensUseTheSame540UnitGeometry()
        {
            var large = Layout(1080f, 2400f);
            var small = Layout(720f, 1600f);

            Assert.That(large.Scale, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(small.Scale, Is.EqualTo(4f / 3f).Within(Tolerance));
            AssertRect(large.Root, small.Root);
            AssertRect(large.TopStatusBar, small.TopStatusBar);
            AssertRect(large.ContentViewport, small.ContentViewport);
            AssertRect(large.BottomTabBar, small.BottomTabBar);
            AssertRect(large.Monitor.Preview, small.Monitor.Preview);
            Assert.That(large.Root.width, Is.EqualTo(540f).Within(Tolerance));
            Assert.That(large.Orientation, Is.EqualTo(BeamProPageOrientation.Portrait));
            Assert.That(large.Monitor.Preview.width / large.Monitor.Preview.height,
                Is.EqualTo(16f / 9f).Within(Tolerance));
            AssertTwoByTwo(large.Monitor.MetricCards);
        }

        [Test]
        public void LandscapePlacesPreviewAndMetricGridSideBySide()
        {
            var layout = Layout(2400f, 1080f);

            Assert.That(layout.Orientation, Is.EqualTo(BeamProPageOrientation.Landscape));
            Assert.That(layout.Root.height, Is.EqualTo(540f).Within(Tolerance));
            Assert.That(layout.Monitor.Preview.width / layout.Monitor.Preview.height,
                Is.EqualTo(16f / 9f).Within(Tolerance));
            AssertTwoByTwo(layout.Monitor.MetricCards);
            foreach (var metric in layout.Monitor.MetricCards)
                Assert.That(metric.xMin, Is.GreaterThanOrEqualTo(layout.Monitor.Preview.xMax + BeamProPageLayoutCalculator.Gap - Tolerance));
            Assert.That(layout.Monitor.AssetStatus.yMin,
                Is.GreaterThanOrEqualTo(Mathf.Max(layout.Monitor.Preview.yMax, layout.Monitor.FollowButton.yMax) +
                                        BeamProPageLayoutCalculator.Gap - Tolerance));
        }

        [Test]
        public void MonitorProvidesSeparateNonOverlappingHoverAndFollowButtons()
        {
            var portrait = Layout(1080f, 2400f);
            var landscape = Layout(2400f, 1080f);

            foreach (var layout in new[] { portrait, landscape })
            {
                Assert.That(layout.Monitor.HoverButton.height,
                    Is.GreaterThanOrEqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
                Assert.That(layout.Monitor.FollowButton.height,
                    Is.GreaterThanOrEqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
                Assert.That(layout.Monitor.HoverButton.y,
                    Is.EqualTo(layout.Monitor.FollowButton.y).Within(Tolerance));
                Assert.That(layout.Monitor.HoverButton.yMin,
                    Is.GreaterThanOrEqualTo(layout.Monitor.HudToggle.yMax +
                                            BeamProPageLayoutCalculator.Gap - Tolerance));
                AssertNoPositiveOverlap(layout.Monitor.HoverButton, layout.Monitor.FollowButton);
                Assert.That(layout.Monitor.AssetStatus.yMin,
                    Is.GreaterThanOrEqualTo(layout.Monitor.FollowButton.yMax +
                                            BeamProPageLayoutCalculator.Gap - Tolerance));
            }
        }

        [Test]
        public void ScrollableLandscapePagesReserveTheVerticalScrollbarWithoutHorizontalOverflow()
        {
            var layout = Layout(2400f, 1080f, true);
            var expectedMaximumWidth = layout.ContentViewport.width - BeamProPageLayoutCalculator.ScrollbarGutter;

            Assert.That(layout.Monitor.ScrollContent.height, Is.GreaterThan(layout.ContentViewport.height));
            Assert.That(layout.Connection.ScrollContent.height, Is.GreaterThan(layout.ContentViewport.height));
            Assert.That(layout.Debug.ScrollContent.height, Is.GreaterThan(layout.ContentViewport.height));
            Assert.That(layout.Monitor.ScrollContent.width, Is.LessThanOrEqualTo(expectedMaximumWidth + Tolerance));
            Assert.That(layout.Connection.ScrollContent.width, Is.LessThanOrEqualTo(expectedMaximumWidth + Tolerance));
            Assert.That(layout.Debug.ScrollContent.width, Is.LessThanOrEqualTo(expectedMaximumWidth + Tolerance));
            Assert.That(layout.Monitor.AssetStatus.xMax,
                Is.LessThanOrEqualTo(layout.Monitor.ScrollContent.width + Tolerance));
            Assert.That(layout.Connection.OperationResult.xMax,
                Is.LessThanOrEqualTo(layout.Connection.ScrollContent.width + Tolerance));
            Assert.That(layout.Debug.Logs.xMax,
                Is.LessThanOrEqualTo(layout.Debug.ScrollContent.width + Tolerance));
        }

        [Test]
        public void SafeAreaOffsetsPhysicalGeometryAndContainsFixedRegions()
        {
            var request = new BeamProPageLayoutRequest(
                new Vector2(1080f, 2400f),
                new Rect(24f, 80f, 1032f, 2240f),
                0f,
                BeamProPage.Monitor,
                true);
            var layout = BeamProPageLayoutCalculator.Calculate(request);

            Assert.That(layout.SafeAreaTopLeftPixels, Is.EqualTo(new Vector2(24f, 80f)));
            Assert.That(layout.VisibleSafeAreaPixels, Is.EqualTo(new Rect(24f, 80f, 1032f, 2240f)));
            AssertPhysicalRectIsContained(layout, layout.TopStatusBar);
            AssertPhysicalRectIsContained(layout, layout.ContentViewport);
            AssertPhysicalRectIsContained(layout, layout.BottomTabBar);
            Assert.That(layout.TabButtons, Has.Length.EqualTo(3));
            AssertNoPositiveOverlap(layout.TopStatusBar, layout.ContentViewport);
            AssertNoPositiveOverlap(layout.ContentViewport, layout.BottomTabBar);
        }

        [Test]
        public void KeyboardInsetMovesTabsAboveKeyboardAndSubmitCanBeRevealed()
        {
            const float keyboardInset = 1500f;
            var layout = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                new Vector2(1080f, 2400f),
                new Rect(0f, 0f, 1080f, 2400f),
                keyboardInset,
                BeamProPage.Connection));
            var tabsPixels = layout.ToScreenPixels(layout.BottomTabBar);
            var keyboardTopPixels = 2400f - keyboardInset;

            Assert.That(tabsPixels.yMax, Is.LessThanOrEqualTo(keyboardTopPixels + Tolerance));
            var offset = BeamProPageLayoutCalculator.ComputeScrollOffsetToReveal(
                layout.ContentViewport.height,
                layout.Connection.ScrollContent.height,
                layout.Connection.ConnectButton,
                0f);
            var visibleTop = offset + BeamProPageLayoutCalculator.Gap;
            var visibleBottom = offset + layout.ContentViewport.height - BeamProPageLayoutCalculator.Gap;
            Assert.That(layout.Connection.ConnectButton.yMin, Is.GreaterThanOrEqualTo(visibleTop - Tolerance));
            Assert.That(layout.Connection.ConnectButton.yMax, Is.LessThanOrEqualTo(visibleBottom + Tolerance));
        }

        [Test]
        public void AllInteractiveRowsMeetMinimumTouchHeightAndDoNotOverlapPeers()
        {
            var layout = Layout(1080f, 2400f, true);

            AssertMinimumTouchHeight(layout.TabButtons);
            AssertMinimumTouchHeight(layout.Monitor.TouchTargets);
            AssertMinimumTouchHeight(layout.Connection.TouchTargets);
            AssertMinimumTouchHeight(layout.Debug.TouchTargets);
            Assert.That(layout.Debug.ControlRows, Has.Length.EqualTo(12));
            AssertPairwiseNoPositiveOverlap(layout.TabButtons);
            AssertPairwiseNoPositiveOverlap(layout.Monitor.TouchTargets);
            AssertPairwiseNoPositiveOverlap(layout.Connection.TouchTargets);
            AssertPairwiseNoPositiveOverlap(layout.Debug.TouchTargets);
            AssertSequentialNoOverlap(
                layout.Connection.ConnectionStatus,
                layout.Connection.IpInput,
                layout.Connection.PortInput,
                layout.Connection.ConnectButton,
                layout.Connection.OperationResult);
            AssertSequentialNoOverlap(
                layout.Debug.DisplayControls,
                layout.Debug.CameraAndGestureControls,
                layout.Debug.NavigationAndMediaControls,
                layout.Debug.Logs);
            AssertRowsAreContained(layout.Debug.DisplayControls, layout.Debug.ControlRows, 0, 3);
            AssertRowsAreContained(layout.Debug.CameraAndGestureControls, layout.Debug.ControlRows, 3, 2);
            AssertRowsAreContained(layout.Debug.NavigationAndMediaControls, layout.Debug.ControlRows, 5, 7);
        }

        [Test]
        public void BottomTabsAndContentRemainSeparateAt720By1600()
        {
            var layout = Layout(720f, 1600f, true);

            Assert.That(layout.ContentViewport.yMin,
                Is.GreaterThanOrEqualTo(layout.TopStatusBar.yMax + BeamProPageLayoutCalculator.Gap - Tolerance));
            Assert.That(layout.ContentViewport.yMax,
                Is.LessThanOrEqualTo(layout.BottomTabBar.yMin - BeamProPageLayoutCalculator.Gap + Tolerance));
            foreach (var tab in layout.TabButtons)
            {
                Assert.That(tab.yMin, Is.GreaterThanOrEqualTo(layout.BottomTabBar.yMin));
                Assert.That(tab.yMax, Is.LessThanOrEqualTo(layout.BottomTabBar.yMax + Tolerance));
            }
        }

        [Test]
        public void ExtremelyLongHeaderIsClampedWithoutCoveringContentOrTabs()
        {
            var layout = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                new Vector2(720f, 1600f),
                new Rect(0f, 0f, 720f, 1600f),
                0f,
                BeamProPage.Monitor,
                true,
                topStatusContentHeight: 5000f));

            Assert.That(layout.ContentViewport.height,
                Is.GreaterThanOrEqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight
                                        + BeamProPageLayoutCalculator.Gap * 2f - Tolerance));
            AssertNoPositiveOverlap(layout.TopStatusBar, layout.ContentViewport);
            AssertNoPositiveOverlap(layout.ContentViewport, layout.BottomTabBar);
            Assert.That(layout.TopStatusBar.yMax + BeamProPageLayoutCalculator.Gap,
                Is.LessThanOrEqualTo(layout.ContentViewport.yMin + Tolerance));
            Assert.That(layout.ContentViewport.yMax + BeamProPageLayoutCalculator.Gap,
                Is.LessThanOrEqualTo(layout.BottomTabBar.yMin + Tolerance));
        }

        [Test]
        public void DebugAndUnknownPagesNormalizeToMonitorWhenUnavailable()
        {
            var debugWithoutEngineerMode = BeamProPageLayoutCalculator.Calculate(
                new BeamProPageLayoutRequest(
                    new Vector2(1080f, 2400f),
                    new Rect(0f, 0f, 1080f, 2400f),
                    0f,
                    BeamProPage.Debug,
                    false));
            var invalidPage = BeamProPageLayoutCalculator.Calculate(
                new BeamProPageLayoutRequest(
                    new Vector2(1080f, 2400f),
                    new Rect(0f, 0f, 1080f, 2400f),
                    0f,
                    (BeamProPage)99,
                    true));
            var debugWithEngineerMode = BeamProPageLayoutCalculator.Calculate(
                new BeamProPageLayoutRequest(
                    new Vector2(1080f, 2400f),
                    new Rect(0f, 0f, 1080f, 2400f),
                    0f,
                    BeamProPage.Debug,
                    true));

            Assert.That(debugWithoutEngineerMode.SelectedPage, Is.EqualTo(BeamProPage.Monitor));
            Assert.That(invalidPage.SelectedPage, Is.EqualTo(BeamProPage.Monitor));
            Assert.That(debugWithEngineerMode.SelectedPage, Is.EqualTo(BeamProPage.Debug));
        }

        [Test]
        public void DebugPageCollapsesControlsDisabledByInspectorConfiguration()
        {
            var full = Layout(1080f, 2400f, true);
            var collapsed = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                new Vector2(1080f, 2400f),
                new Rect(0f, 0f, 1080f, 2400f),
                selectedPage: BeamProPage.Debug,
                engineerMode: true,
                debugShowInputControl: false,
                debugShowGestureControl: false,
                debugShowObjectMoveControls: false,
                debugShowPlaneControls: false));

            Assert.That(collapsed.Debug.ControlRows[0].height,
                Is.EqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
            Assert.That(collapsed.Debug.ControlRows[1].height,
                Is.EqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
            Assert.That(collapsed.Debug.ControlRows[3].height,
                Is.EqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
            Assert.That(collapsed.Debug.ControlRows[2], Is.EqualTo(Rect.zero));
            Assert.That(collapsed.Debug.ControlRows[4], Is.EqualTo(Rect.zero));
            for (var index = 5; index < collapsed.Debug.ControlRows.Length; index++)
                Assert.That(collapsed.Debug.ControlRows[index], Is.EqualTo(Rect.zero));
            Assert.That(collapsed.Debug.NavigationAndMediaControls, Is.EqualTo(Rect.zero));
            Assert.That(collapsed.Debug.Logs.yMin, Is.LessThan(full.Debug.Logs.yMin));
        }

        [Test]
        public void MeasuredWrappedTextHeightsExpandFinalBlocksAndScrollContent()
        {
            var baseline = Layout(1080f, 2400f, true);
            var expanded = BeamProPageLayoutCalculator.Calculate(
                new BeamProPageLayoutRequest(
                    new Vector2(1080f, 2400f),
                    new Rect(0f, 0f, 1080f, 2400f),
                    0f,
                    BeamProPage.Monitor,
                    true,
                    220f,
                    180f,
                    260f,
                    420f,
                    140f));

            Assert.That(expanded.TopStatusBar.height, Is.EqualTo(140f));
            Assert.That(expanded.ContentViewport.yMin,
                Is.EqualTo(expanded.TopStatusBar.yMax + BeamProPageLayoutCalculator.Gap).Within(Tolerance));
            Assert.That(expanded.Monitor.AssetStatus.height, Is.EqualTo(220f));
            Assert.That(expanded.Connection.ConnectionStatus.height, Is.EqualTo(180f));
            Assert.That(expanded.Connection.OperationResult.height, Is.EqualTo(260f));
            Assert.That(expanded.Debug.Logs.height, Is.EqualTo(420f));
            Assert.That(expanded.Monitor.ScrollContent.height, Is.GreaterThan(baseline.Monitor.ScrollContent.height));
            Assert.That(expanded.Connection.ScrollContent.height, Is.GreaterThan(baseline.Connection.ScrollContent.height));
            Assert.That(expanded.Debug.ScrollContent.height, Is.GreaterThan(baseline.Debug.ScrollContent.height));
            Assert.That(expanded.Connection.IpInput.height, Is.GreaterThanOrEqualTo(84f));
            Assert.That(expanded.Connection.PortInput.height, Is.GreaterThanOrEqualTo(84f));
        }

        static BeamProPageLayoutResult Layout(float width, float height, bool engineerMode = false)
        {
            return BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                new Vector2(width, height),
                new Rect(0f, 0f, width, height),
                0f,
                BeamProPage.Monitor,
                engineerMode));
        }

        static void AssertRect(Rect actual, Rect expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(Tolerance));
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(Tolerance));
        }

        static void AssertTwoByTwo(Rect[] cards)
        {
            Assert.That(cards, Has.Length.EqualTo(4));
            Assert.That(cards[0].y, Is.EqualTo(cards[1].y).Within(Tolerance));
            Assert.That(cards[2].y, Is.EqualTo(cards[3].y).Within(Tolerance));
            Assert.That(cards[2].yMin, Is.GreaterThanOrEqualTo(cards[0].yMax +
                                                               BeamProPageLayoutCalculator.Gap - Tolerance));
            AssertNoPositiveOverlap(cards[0], cards[1]);
            AssertNoPositiveOverlap(cards[0], cards[2]);
            AssertNoPositiveOverlap(cards[1], cards[3]);
            AssertNoPositiveOverlap(cards[2], cards[3]);
        }

        static void AssertPhysicalRectIsContained(BeamProPageLayoutResult layout, Rect logicalRect)
        {
            var physical = layout.ToScreenPixels(logicalRect);
            Assert.That(physical.xMin, Is.GreaterThanOrEqualTo(layout.VisibleSafeAreaPixels.xMin - Tolerance));
            Assert.That(physical.yMin, Is.GreaterThanOrEqualTo(layout.VisibleSafeAreaPixels.yMin - Tolerance));
            Assert.That(physical.xMax, Is.LessThanOrEqualTo(layout.VisibleSafeAreaPixels.xMax + Tolerance));
            Assert.That(physical.yMax, Is.LessThanOrEqualTo(layout.VisibleSafeAreaPixels.yMax + Tolerance));
        }

        static void AssertMinimumTouchHeight(Rect[] targets)
        {
            foreach (var target in targets)
                Assert.That(target.height, Is.GreaterThanOrEqualTo(BeamProPageLayoutCalculator.MinimumTouchHeight));
        }

        static void AssertRowsAreContained(Rect group, Rect[] rows, int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                Assert.That(rows[i].xMin, Is.GreaterThanOrEqualTo(group.xMin - Tolerance));
                Assert.That(rows[i].yMin, Is.GreaterThanOrEqualTo(group.yMin - Tolerance));
                Assert.That(rows[i].xMax, Is.LessThanOrEqualTo(group.xMax + Tolerance));
                Assert.That(rows[i].yMax, Is.LessThanOrEqualTo(group.yMax + Tolerance));
            }
        }

        static void AssertSequentialNoOverlap(params Rect[] rects)
        {
            for (var i = 1; i < rects.Length; i++)
                Assert.That(rects[i].yMin, Is.GreaterThanOrEqualTo(rects[i - 1].yMax - Tolerance));
        }

        static void AssertPairwiseNoPositiveOverlap(Rect[] rects)
        {
            for (var i = 0; i < rects.Length; i++)
            for (var j = i + 1; j < rects.Length; j++)
                AssertNoPositiveOverlap(rects[i], rects[j]);
        }

        static void AssertNoPositiveOverlap(Rect a, Rect b)
        {
            var overlapWidth = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            var overlapHeight = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            Assert.That(overlapWidth <= Tolerance || overlapHeight <= Tolerance, Is.True,
                "Expected no overlap, but {0} overlaps {1}.", a, b);
        }
    }
}
