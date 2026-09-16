using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public enum BeamProPage
    {
        Monitor = 0,
        Connection = 1,
        Debug = 2,
    }

    public enum BeamProPageOrientation
    {
        Portrait = 0,
        Landscape = 1,
    }

    /// <summary>
    /// Immutable inputs for the Beam Pro paged UI layout.
    /// SafeAreaPixels uses Unity's bottom-left screen coordinates. All returned layout rectangles use
    /// logical units with a top-left origin local to the visible safe area.
    /// </summary>
    public struct BeamProPageLayoutRequest
    {
        public Vector2 ScreenSizePixels;
        public Rect SafeAreaPixels;
        public float KeyboardInsetPixels;
        public BeamProPage SelectedPage;
        public bool EngineerMode;
        public float MonitorStatusHeight;
        public float ConnectionStatusHeight;
        public float ConnectionResultHeight;
        public float DebugLogHeight;
        public float TopStatusContentHeight;
        public bool DebugShowInputControl;
        public bool DebugShowGestureControl;
        public bool DebugShowObjectMoveControls;
        public bool DebugShowPlaneControls;

        public BeamProPageLayoutRequest(
            Vector2 screenSizePixels,
            Rect safeAreaPixels,
            float keyboardInsetPixels = 0f,
            BeamProPage selectedPage = BeamProPage.Monitor,
            bool engineerMode = false,
            float monitorStatusHeight = 0f,
            float connectionStatusHeight = 0f,
            float connectionResultHeight = 0f,
            float debugLogHeight = 0f,
            float topStatusContentHeight = 0f,
            bool debugShowInputControl = true,
            bool debugShowGestureControl = true,
            bool debugShowObjectMoveControls = true,
            bool debugShowPlaneControls = true)
        {
            ScreenSizePixels = screenSizePixels;
            SafeAreaPixels = safeAreaPixels;
            KeyboardInsetPixels = keyboardInsetPixels;
            SelectedPage = selectedPage;
            EngineerMode = engineerMode;
            MonitorStatusHeight = monitorStatusHeight;
            ConnectionStatusHeight = connectionStatusHeight;
            ConnectionResultHeight = connectionResultHeight;
            DebugLogHeight = debugLogHeight;
            TopStatusContentHeight = topStatusContentHeight;
            DebugShowInputControl = debugShowInputControl;
            DebugShowGestureControl = debugShowGestureControl;
            DebugShowObjectMoveControls = debugShowObjectMoveControls;
            DebugShowPlaneControls = debugShowPlaneControls;
        }
    }

    public sealed class BeamProMonitorPageLayout
    {
        public Rect ScrollContent { get; internal set; }
        public Rect Preview { get; internal set; }
        public Rect[] MetricCards { get; internal set; }
        public Rect HudToggle { get; internal set; }
        public Rect ModelToggle { get; internal set; }
        public Rect AssetStatus { get; internal set; }

        public Rect[] TouchTargets
        {
            get { return new[] { HudToggle, ModelToggle }; }
        }
    }

    public sealed class BeamProConnectionPageLayout
    {
        public Rect ScrollContent { get; internal set; }
        public Rect ConnectionStatus { get; internal set; }
        public Rect IpInput { get; internal set; }
        public Rect PortInput { get; internal set; }
        public Rect ConnectButton { get; internal set; }
        public Rect OperationResult { get; internal set; }

        public Rect[] TouchTargets
        {
            get { return new[] { IpInput, PortInput, ConnectButton }; }
        }
    }

    public sealed class BeamProDebugPageLayout
    {
        public Rect ScrollContent { get; internal set; }
        public Rect DisplayControls { get; internal set; }
        public Rect CameraAndGestureControls { get; internal set; }
        public Rect NavigationAndMediaControls { get; internal set; }
        public Rect Logs { get; internal set; }
        public Rect[] ControlRows { get; internal set; }

        public Rect[] TouchTargets
        {
            get { return ControlRows; }
        }
    }

    /// <summary>
    /// Complete geometry for the paged Beam Pro UI. Fixed rectangles are relative to the visible safe
    /// area. Page-specific rectangles are relative to their page's ScrollContent and are intended for
    /// use inside a single GUI scroll view backed by ContentViewport.
    /// </summary>
    public sealed class BeamProPageLayoutResult
    {
        public float Scale { get; internal set; }
        public Vector2 SafeAreaTopLeftPixels { get; internal set; }
        public Rect VisibleSafeAreaPixels { get; internal set; }
        public Rect Root { get; internal set; }
        public Rect TopStatusBar { get; internal set; }
        public Rect ContentViewport { get; internal set; }
        public Rect BottomTabBar { get; internal set; }
        public Rect[] TabButtons { get; internal set; }
        public BeamProPage SelectedPage { get; internal set; }
        public BeamProPageOrientation Orientation { get; internal set; }
        public BeamProMonitorPageLayout Monitor { get; internal set; }
        public BeamProConnectionPageLayout Connection { get; internal set; }
        public BeamProDebugPageLayout Debug { get; internal set; }

        public Rect ToScreenPixels(Rect logicalRect)
        {
            return new Rect(
                SafeAreaTopLeftPixels.x + logicalRect.x * Scale,
                SafeAreaTopLeftPixels.y + logicalRect.y * Scale,
                logicalRect.width * Scale,
                logicalRect.height * Scale);
        }
    }

    /// <summary>
    /// Pure layout calculator for Beam Pro IMGUI pages. It does not read Screen or GUI state, making
    /// the geometry deterministic and directly testable in EditMode.
    /// </summary>
    public static class BeamProPageLayoutCalculator
    {
        public const float ReferenceShortSide = 540f;
        public const float OuterMargin = 20f;
        public const float Gap = 12f;
        public const float MinimumTouchHeight = 56f;
        public const float TopStatusHeight = 72f;
        public const float BottomTabsHeight = 72f;
        public const float ScrollbarGutter = 20f;

        public static BeamProPageLayoutResult Calculate(BeamProPageLayoutRequest request)
        {
            var screenWidth = Mathf.Max(1f, request.ScreenSizePixels.x);
            var screenHeight = Mathf.Max(1f, request.ScreenSizePixels.y);
            var safeArea = ClampSafeArea(request.SafeAreaPixels, screenWidth, screenHeight);
            var scale = Mathf.Max(0.01f, Mathf.Min(safeArea.width, safeArea.height) / ReferenceShortSide);
            var totalLogicalHeight = safeArea.height / scale;
            var minimumContentHeight = MinimumTouchHeight + Gap * 2f;
            var maximumTopStatusHeight = Mathf.Max(
                TopStatusHeight,
                totalLogicalHeight - Gap - minimumContentHeight - Gap - BottomTabsHeight);
            var topStatusHeight = Mathf.Clamp(
                Mathf.Max(TopStatusHeight, request.TopStatusContentHeight),
                TopStatusHeight,
                maximumTopStatusHeight);
            var minimumVisibleLogicalHeight =
                topStatusHeight + Gap + minimumContentHeight + Gap + BottomTabsHeight;
            var maximumKeyboardInset = Mathf.Max(0f, safeArea.height - minimumVisibleLogicalHeight * scale);
            var keyboardInset = Mathf.Clamp(request.KeyboardInsetPixels, 0f, maximumKeyboardInset);
            var visiblePixelHeight = safeArea.height - keyboardInset;
            var logicalWidth = safeArea.width / scale;
            var logicalHeight = visiblePixelHeight / scale;
            var orientation = safeArea.width > safeArea.height
                ? BeamProPageOrientation.Landscape
                : BeamProPageOrientation.Portrait;

            var root = new Rect(0f, 0f, logicalWidth, logicalHeight);
            var topStatus = new Rect(0f, 0f, logicalWidth, topStatusHeight);
            var bottomTabs = new Rect(0f, logicalHeight - BottomTabsHeight, logicalWidth, BottomTabsHeight);
            var content = new Rect(
                OuterMargin,
                topStatus.yMax + Gap,
                Mathf.Max(MinimumTouchHeight, logicalWidth - OuterMargin * 2f),
                Mathf.Max(minimumContentHeight, bottomTabs.yMin - Gap - (topStatus.yMax + Gap)));
            var safeTop = screenHeight - safeArea.yMax;

            var result = new BeamProPageLayoutResult
            {
                Scale = scale,
                SafeAreaTopLeftPixels = new Vector2(safeArea.xMin, safeTop),
                VisibleSafeAreaPixels = new Rect(safeArea.xMin, safeTop, safeArea.width, visiblePixelHeight),
                Root = root,
                TopStatusBar = topStatus,
                ContentViewport = content,
                BottomTabBar = bottomTabs,
                TabButtons = CreateTabButtons(logicalWidth, bottomTabs.yMin, request.EngineerMode ? 3 : 2),
                SelectedPage = NormalizeSelectedPage(request.SelectedPage, request.EngineerMode),
                Orientation = orientation,
            };

            result.Monitor = CreateMonitorPage(content.size, orientation, request.MonitorStatusHeight);
            if (result.Monitor.ScrollContent.height > content.height)
            {
                result.Monitor = CreateMonitorPage(
                    new Vector2(Mathf.Max(MinimumTouchHeight, content.width - ScrollbarGutter), content.height),
                    orientation,
                    request.MonitorStatusHeight);
            }

            result.Connection = CreateConnectionPage(
                content.size,
                request.ConnectionStatusHeight,
                request.ConnectionResultHeight);
            if (result.Connection.ScrollContent.height > content.height)
            {
                result.Connection = CreateConnectionPage(
                    new Vector2(Mathf.Max(MinimumTouchHeight, content.width - ScrollbarGutter), content.height),
                    request.ConnectionStatusHeight,
                    request.ConnectionResultHeight);
            }

            result.Debug = CreateDebugPage(
                content.size,
                request.DebugLogHeight,
                request.DebugShowInputControl,
                request.DebugShowGestureControl,
                request.DebugShowObjectMoveControls,
                request.DebugShowPlaneControls);
            if (result.Debug.ScrollContent.height > content.height)
            {
                result.Debug = CreateDebugPage(
                    new Vector2(Mathf.Max(MinimumTouchHeight, content.width - ScrollbarGutter), content.height),
                    request.DebugLogHeight,
                    request.DebugShowInputControl,
                    request.DebugShowGestureControl,
                    request.DebugShowObjectMoveControls,
                    request.DebugShowPlaneControls);
            }
            return result;
        }

        /// <summary>
        /// Returns a clamped scroll offset that makes targetContentRect visible in the viewport.
        /// Both target and scroll offset use page-content coordinates.
        /// </summary>
        public static float ComputeScrollOffsetToReveal(
            float viewportHeight,
            float contentHeight,
            Rect targetContentRect,
            float currentOffset,
            float padding = Gap)
        {
            viewportHeight = Mathf.Max(MinimumTouchHeight, viewportHeight);
            contentHeight = Mathf.Max(viewportHeight, contentHeight);
            padding = Mathf.Max(0f, padding);
            var maximumOffset = Mathf.Max(0f, contentHeight - viewportHeight);
            var offset = Mathf.Clamp(currentOffset, 0f, maximumOffset);
            var visibleTop = offset + padding;
            var visibleBottom = offset + viewportHeight - padding;

            if (targetContentRect.yMin < visibleTop)
                offset = targetContentRect.yMin - padding;
            else if (targetContentRect.yMax > visibleBottom)
                offset = targetContentRect.yMax + padding - viewportHeight;

            return Mathf.Clamp(offset, 0f, maximumOffset);
        }

        static Rect ClampSafeArea(Rect requested, float screenWidth, float screenHeight)
        {
            if (requested.width <= 0f || requested.height <= 0f)
                return new Rect(0f, 0f, screenWidth, screenHeight);

            var xMin = Mathf.Clamp(requested.xMin, 0f, screenWidth);
            var yMin = Mathf.Clamp(requested.yMin, 0f, screenHeight);
            var xMax = Mathf.Clamp(requested.xMax, xMin, screenWidth);
            var yMax = Mathf.Clamp(requested.yMax, yMin, screenHeight);
            if (xMax - xMin < 1f || yMax - yMin < 1f)
                return new Rect(0f, 0f, screenWidth, screenHeight);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static BeamProPage NormalizeSelectedPage(BeamProPage requested, bool engineerMode)
        {
            if (requested == BeamProPage.Connection)
                return BeamProPage.Connection;
            if (requested == BeamProPage.Debug && engineerMode)
                return BeamProPage.Debug;
            return BeamProPage.Monitor;
        }

        static Rect[] CreateTabButtons(float logicalWidth, float tabBarY, int count)
        {
            var buttons = new Rect[count];
            var availableWidth = logicalWidth - OuterMargin * 2f - Gap * (count - 1);
            var buttonWidth = availableWidth / count;
            var y = tabBarY + 8f;
            for (var i = 0; i < count; i++)
            {
                buttons[i] = new Rect(
                    OuterMargin + i * (buttonWidth + Gap),
                    y,
                    buttonWidth,
                    MinimumTouchHeight);
            }
            return buttons;
        }

        static BeamProMonitorPageLayout CreateMonitorPage(
            Vector2 viewportSize,
            BeamProPageOrientation orientation,
            float requestedStatusHeight)
        {
            return orientation == BeamProPageOrientation.Portrait
                ? CreatePortraitMonitorPage(viewportSize, requestedStatusHeight)
                : CreateLandscapeMonitorPage(viewportSize, requestedStatusHeight);
        }

        static BeamProMonitorPageLayout CreatePortraitMonitorPage(
            Vector2 viewportSize,
            float requestedStatusHeight)
        {
            var width = viewportSize.x;
            var y = 0f;
            var preview = new Rect(0f, y, width, width * 9f / 16f);
            y = preview.yMax + Gap;

            var cardWidth = (width - Gap) * 0.5f;
            const float cardHeight = 116f;
            var metrics = new[]
            {
                new Rect(0f, y, cardWidth, cardHeight),
                new Rect(cardWidth + Gap, y, cardWidth, cardHeight),
                new Rect(0f, y + cardHeight + Gap, cardWidth, cardHeight),
                new Rect(cardWidth + Gap, y + cardHeight + Gap, cardWidth, cardHeight),
            };
            y = metrics[3].yMax + Gap;

            var controlWidth = (width - Gap) * 0.5f;
            var hudToggle = new Rect(0f, y, controlWidth, MinimumTouchHeight);
            var modelToggle = new Rect(controlWidth + Gap, y, controlWidth, MinimumTouchHeight);
            y = hudToggle.yMax + Gap;

            var assetStatus = new Rect(0f, y, width, Mathf.Max(116f, requestedStatusHeight));
            y = assetStatus.yMax + OuterMargin;
            return new BeamProMonitorPageLayout
            {
                ScrollContent = new Rect(0f, 0f, width, y),
                Preview = preview,
                MetricCards = metrics,
                HudToggle = hudToggle,
                ModelToggle = modelToggle,
                AssetStatus = assetStatus,
            };
        }

        static BeamProMonitorPageLayout CreateLandscapeMonitorPage(
            Vector2 viewportSize,
            float requestedStatusHeight)
        {
            var width = viewportSize.x;
            var targetPreviewWidth = Mathf.Min((width - Gap) * 0.58f, viewportSize.y * 16f / 9f);
            var previewWidth = Mathf.Clamp(targetPreviewWidth, 280f, Mathf.Max(280f, width - Gap - 320f));
            var rightX = previewWidth + Gap;
            var rightWidth = width - rightX;
            var preview = new Rect(0f, 0f, previewWidth, previewWidth * 9f / 16f);

            var cardWidth = (rightWidth - Gap) * 0.5f;
            const float cardHeight = 112f;
            var metrics = new[]
            {
                new Rect(rightX, 0f, cardWidth, cardHeight),
                new Rect(rightX + cardWidth + Gap, 0f, cardWidth, cardHeight),
                new Rect(rightX, cardHeight + Gap, cardWidth, cardHeight),
                new Rect(rightX + cardWidth + Gap, cardHeight + Gap, cardWidth, cardHeight),
            };

            var controlsY = metrics[3].yMax + Gap;
            var controlWidth = (rightWidth - Gap) * 0.5f;
            var hudToggle = new Rect(rightX, controlsY, controlWidth, MinimumTouchHeight);
            var modelToggle = new Rect(rightX + controlWidth + Gap, controlsY, controlWidth, MinimumTouchHeight);
            var firstRowBottom = Mathf.Max(preview.yMax, hudToggle.yMax);
            var assetStatus = new Rect(0f, firstRowBottom + Gap, width, Mathf.Max(104f, requestedStatusHeight));
            var contentHeight = assetStatus.yMax + OuterMargin;

            return new BeamProMonitorPageLayout
            {
                ScrollContent = new Rect(0f, 0f, width, contentHeight),
                Preview = preview,
                MetricCards = metrics,
                HudToggle = hudToggle,
                ModelToggle = modelToggle,
                AssetStatus = assetStatus,
            };
        }

        static BeamProConnectionPageLayout CreateConnectionPage(
            Vector2 viewportSize,
            float requestedStatusHeight,
            float requestedResultHeight)
        {
            var width = Mathf.Min(720f, viewportSize.x);
            var x = (viewportSize.x - width) * 0.5f;
            var y = 0f;
            var status = new Rect(x, y, width, Mathf.Max(92f, requestedStatusHeight));
            y = status.yMax + Gap;
            var ip = new Rect(x, y, width, 84f);
            y = ip.yMax + Gap;
            var port = new Rect(x, y, width, 84f);
            y = port.yMax + Gap;
            var connect = new Rect(x, y, width, MinimumTouchHeight);
            y = connect.yMax + Gap;
            var result = new Rect(x, y, width, Mathf.Max(120f, requestedResultHeight));
            y = result.yMax + OuterMargin;

            return new BeamProConnectionPageLayout
            {
                ScrollContent = new Rect(0f, 0f, viewportSize.x, y),
                ConnectionStatus = status,
                IpInput = ip,
                PortInput = port,
                ConnectButton = connect,
                OperationResult = result,
            };
        }

        static BeamProDebugPageLayout CreateDebugPage(
            Vector2 viewportSize,
            float requestedLogHeight,
            bool showInputControl,
            bool showGestureControl,
            bool showObjectMoveControls,
            bool showPlaneControls)
        {
            var width = Mathf.Min(900f, viewportSize.x);
            var x = (viewportSize.x - width) * 0.5f;
            var y = 0f;
            var rows = new Rect[12];
            var display = CreateDebugGroup(x, ref y, width, rows,
                showInputControl ? new[] { 0, 1, 2 } : new[] { 0, 1 });
            var camera = CreateDebugGroup(x, ref y, width, rows,
                showGestureControl ? new[] { 3, 4 } : new[] { 3 });
            int[] navigationRows;
            if (showObjectMoveControls && showPlaneControls)
                navigationRows = new[] { 5, 6, 7, 8, 9, 10, 11 };
            else if (showObjectMoveControls)
                navigationRows = new[] { 5, 6, 7 };
            else if (showPlaneControls)
                navigationRows = new[] { 8, 9, 10, 11 };
            else
                navigationRows = new int[0];
            var navigation = CreateDebugGroup(x, ref y, width, rows, navigationRows);
            var logs = new Rect(x, y, width, Mathf.Max(280f, requestedLogHeight));
            y = logs.yMax + OuterMargin;

            return new BeamProDebugPageLayout
            {
                ScrollContent = new Rect(0f, 0f, viewportSize.x, y),
                DisplayControls = display,
                CameraAndGestureControls = camera,
                NavigationAndMediaControls = navigation,
                Logs = logs,
                ControlRows = rows,
            };
        }

        static Rect CreateDebugGroup(
            float x,
            ref float y,
            float width,
            Rect[] rows,
            int[] rowIndices)
        {
            if (rowIndices == null || rowIndices.Length == 0)
                return Rect.zero;

            const float topContentOffset = 56f;
            const float groupBottomPadding = 20f;
            var groupTop = y;
            for (var i = 0; i < rowIndices.Length; i++)
            {
                var rowY = groupTop + topContentOffset + i * (MinimumTouchHeight + Gap);
                rows[rowIndices[i]] = new Rect(
                    x + OuterMargin,
                    rowY,
                    width - OuterMargin * 2f,
                    MinimumTouchHeight);
            }
            var lastRow = rows[rowIndices[rowIndices.Length - 1]];
            var group = new Rect(x, groupTop, width,
                lastRow.yMax + groupBottomPadding - groupTop);
            y = group.yMax + Gap;
            return group;
        }
    }
}
