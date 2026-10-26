using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Single owner for the Beam Pro phone UI. Existing components continue to own navigation,
    /// capture and transport state; this controller only arranges and operates their phone surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeamProPagedController : MonoBehaviour
    {
        const float DependencyRefreshSeconds = 0.5f;
        const float ConnectionAttemptTimeoutSeconds = 12f;
        const string HostControlName = "BeamProConnectionHost";
        const string PortControlName = "BeamProConnectionPort";

        static readonly Color Background = new Color(0.025f, 0.035f, 0.05f, 1f);
        static readonly Color HeaderBackground = new Color(0.045f, 0.065f, 0.085f, 1f);
        static readonly Color PanelBackground = new Color(0.075f, 0.095f, 0.125f, 0.98f);
        static readonly Color PreviewBackground = new Color(0.01f, 0.015f, 0.022f, 1f);
        static readonly Color TextPrimary = Color.white;
        static readonly Color TextSecondary = new Color(0.68f, 0.74f, 0.79f, 1f);
        static readonly Color Green = new Color(0.239f, 0.863f, 0.592f, 1f);
        static readonly Color Amber = new Color(0.961f, 0.773f, 0.094f, 1f);
        static readonly Color Red = new Color(1f, 0.302f, 0.302f, 1f);
        static readonly Color Blue = new Color(0.27f, 0.69f, 0.96f, 1f);
        static readonly Color Disabled = new Color(0.42f, 0.46f, 0.50f, 1f);

        static BeamProPagedController s_Instance;
        static readonly string[] TabLabels = { "Monitor", "Connection", "Debug" };

        HelloMR m_Owner;
        DentalRobotBeamProDisplay m_RobotDisplay;
        LeftEyeDisplayWindow m_LeftEye;
        bool m_EngineerMode;
        bool m_ShowInEditorPreview;
        BeamProPage m_SelectedPage = BeamProPage.Monitor;
        Vector2 m_MonitorScroll;
        Vector2 m_ConnectionScroll;
        Vector2 m_DebugScroll;
        string m_EditableHost;
        string m_EditablePort;
        string m_ConnectionResult = "No manual connection requested yet.";
        bool m_DraftsInitialized;
        bool m_ConnectionRequestInFlight;
        bool m_ConnectionSawConnecting;
        DentalLinkState m_ConnectionInitialLink;
        string m_ConnectionPendingStatus;
        float m_ConnectionRequestStartedRealtime;
        float m_NextDependencyRefreshRealtime;

        BeamProPageLayoutResult m_CachedLayout;
        BeamProPageLayoutRequest m_LayoutRequest;
        string m_LayoutAssetText;
        string m_LayoutConnectionText;
        string m_LayoutOperationText;
        string m_LayoutLogs;
        string m_LayoutContext;
        string m_LayoutAlert;
        float m_HeaderContextHeight;
        readonly GUIContent m_MeasureContent = new GUIContent();
        DentalNavigationSnapshot m_AssetSnapshot;
        string m_AssetCtStatus;
        string m_AssetTransfer;
        string m_AssetText;
        DentalLinkState m_ConnectionLink;
        string m_ConnectionEndpoint;
        string m_ConnectionDetail;
        string m_ConnectionText;
        bool m_HeaderHasContext;
        string m_HeaderTooth;
        string m_HeaderPlan;
        string m_HeaderContext;

        GUIStyle m_HeaderTitleStyle;
        GUIStyle m_HeaderContextStyle;
        GUIStyle m_HeaderAlarmStyle;
        GUIStyle m_SectionTitleStyle;
        GUIStyle m_BodyStyle;
        GUIStyle m_CaptionStyle;
        GUIStyle m_ValueStyle;
        GUIStyle m_ButtonStyle;
        GUIStyle m_TabStyle;
        GUIStyle m_FieldStyle;
        GUIStyle m_PanelStyle;

        public static BeamProPagedController Instance => s_Instance;
        public static bool IsActive => s_Instance != null && s_Instance.ShouldRender;
        public static bool IsLeftEyePreviewVisible => IsActive && s_Instance.m_SelectedPage == BeamProPage.Monitor;
        public BeamProPage SelectedPage => m_SelectedPage;

#if UNITY_EDITOR
        public void SelectEditorPreviewPage(BeamProPage page)
        {
            if (!Application.isEditor)
                return;

            m_EngineerMode = m_Owner != null && m_Owner.EngineerMode;
            if (page == BeamProPage.Debug)
                m_EngineerMode = true;
            m_SelectedPage = page;
        }
#endif

        bool ShouldRender => isActiveAndEnabled
            && (Application.platform == RuntimePlatform.Android
                || (Application.isEditor && m_ShowInEditorPreview));

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            ResolveDependencies(true);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void Configure(HelloMR owner, bool engineerMode, bool showInEditorPreview)
        {
            m_Owner = owner;
            m_EngineerMode = engineerMode;
            m_ShowInEditorPreview = showInEditorPreview;
            if (!m_EngineerMode && m_SelectedPage == BeamProPage.Debug)
                m_SelectedPage = BeamProPage.Monitor;
            ResolveDependencies(true);
            InitializeConnectionDrafts();
        }

        void Update()
        {
            ResolveDependencies(false);
            if (!m_ConnectionRequestInFlight)
                return;

            var state = DentalNavigationState.Instance;
            var link = state != null
                ? state.Capture(Time.realtimeSinceStartup).Link
                : DentalLinkState.Idle;
            if (link == DentalLinkState.Connecting)
            {
                m_ConnectionSawConnecting = true;
                return;
            }

            var elapsed = Time.realtimeSinceStartup - m_ConnectionRequestStartedRealtime;
            var statusChanged = m_RobotDisplay != null
                && !string.Equals(m_RobotDisplay.ConnectionStatus, m_ConnectionPendingStatus,
                    System.StringComparison.Ordinal);
            if (link == DentalLinkState.Live
                && (m_ConnectionSawConnecting || m_ConnectionInitialLink != DentalLinkState.Live))
            {
                FinishConnectionRequest($"Connected to {m_RobotDisplay?.ServerAddress ?? string.Empty}".TrimEnd());
            }
            else if (link == DentalLinkState.Lost
                     && (m_ConnectionSawConnecting
                         || m_ConnectionInitialLink != DentalLinkState.Lost
                         || statusChanged))
            {
                FinishConnectionRequest(m_RobotDisplay != null
                    ? UserFacingStatus(m_RobotDisplay.ConnectionStatus)
                    : "Connection failed. Check the navigation software and local network.");
            }
            else if (elapsed >= ConnectionAttemptTimeoutSeconds)
            {
                FinishConnectionRequest("Connection timed out. Check the IP address, port, and navigation software.");
            }
        }

        void FinishConnectionRequest(string result)
        {
            m_ConnectionRequestInFlight = false;
            m_ConnectionSawConnecting = false;
            m_ConnectionPendingStatus = null;
            m_ConnectionResult = string.IsNullOrEmpty(result) ? "Connection ended." : result;
        }

        void ResolveDependencies(bool force)
        {
            if (!force && Time.realtimeSinceStartup < m_NextDependencyRefreshRealtime)
                return;

            m_NextDependencyRefreshRealtime = Time.realtimeSinceStartup + DependencyRefreshSeconds;
            if (m_Owner == null)
                m_Owner = FindFirstObjectByType<HelloMR>();
            if (m_RobotDisplay == null)
                m_RobotDisplay = DentalRobotBeamProDisplay.Instance != null
                    ? DentalRobotBeamProDisplay.Instance
                    : FindFirstObjectByType<DentalRobotBeamProDisplay>();
            if (m_LeftEye == null)
                m_LeftEye = FindFirstObjectByType<LeftEyeDisplayWindow>();
            InitializeConnectionDrafts();
        }

        void InitializeConnectionDrafts()
        {
            if (m_DraftsInitialized || m_RobotDisplay == null)
                return;
            m_EditableHost = m_RobotDisplay.ServerHost;
            m_EditablePort = m_RobotDisplay.ServerPort.ToString();
            m_DraftsInitialized = true;
        }

        void OnGUI()
        {
            if (!ShouldRender)
                return;

            ResolveDependencies(false);
            EnsureStyles();

            var state = DentalNavigationState.Instance;
            var snapshot = state != null
                ? state.Capture(Time.realtimeSinceStartup)
                : default;
            var evaluation = state != null ? state.LastEvaluation : default;
            var keyboardInset = TouchScreenKeyboard.visible
                ? Mathf.Max(0f, TouchScreenKeyboard.area.height)
                : 0f;
            var safeArea = Screen.safeArea.width > 0f && Screen.safeArea.height > 0f
                ? Screen.safeArea
                : new Rect(0f, 0f, Screen.width, Screen.height);
            var debugShowInput = m_Owner != null && m_Owner.ShowBeamProInputToggle;
            var debugShowGesture = m_Owner != null && m_Owner.ShowBeamProGestureToggle;
            var debugShowMove = m_Owner != null && m_Owner.ShowBeamProObjectMoveButtons;
            var debugShowPlane = m_Owner != null && m_Owner.ShowBeamProCheckPlaneAppearanceButtons;

            if (!m_EngineerMode && m_SelectedPage == BeamProPage.Debug)
                m_SelectedPage = BeamProPage.Monitor;
            var initialRequest = new BeamProPageLayoutRequest(
                new Vector2(Screen.width, Screen.height),
                safeArea,
                keyboardInset,
                m_SelectedPage,
                m_EngineerMode,
                debugShowInputControl: debugShowInput,
                debugShowGestureControl: debugShowGesture,
                debugShowObjectMoveControls: debugShowMove,
                debugShowPlaneControls: debugShowPlane);
            // Inactive pages must not join logs, format status, or measure wrapped text.
            var assetText = m_SelectedPage == BeamProPage.Monitor ? BuildAssetStatus(snapshot) : string.Empty;
            var connectionStatusText = m_SelectedPage == BeamProPage.Connection
                ? BuildConnectionStatus(snapshot) : string.Empty;
            var operationText = m_SelectedPage == BeamProPage.Connection
                ? string.IsNullOrEmpty(m_ConnectionResult) ? "No manual connection requested yet." : m_ConnectionResult
                : string.Empty;
            var logs = m_SelectedPage == BeamProPage.Debug ? BeamProUnifiedLogWindow.SnapshotText : string.Empty;
            var headerContext = HeaderContext(snapshot);
            var headerAlert = HeaderAlert(evaluation);
            var layout = GetLayout(initialRequest, assetText, connectionStatusText, operationText,
                logs, headerContext, headerAlert);
            m_SelectedPage = layout.SelectedPage;

            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            var oldContentColor = GUI.contentColor;
            var oldBackgroundColor = GUI.backgroundColor;
            var oldEnabled = GUI.enabled;
            var oldDepth = GUI.depth;

            try
            {
                GUI.depth = -100;
                GUI.matrix = Matrix4x4.TRS(
                    new Vector3(layout.SafeAreaTopLeftPixels.x, layout.SafeAreaTopLeftPixels.y, 0f),
                    Quaternion.identity,
                    new Vector3(layout.Scale, layout.Scale, 1f));
                DrawFilledRect(layout.Root, Background);
                DrawHeader(layout.TopStatusBar, layout.ExitButton, snapshot, evaluation, headerContext, headerAlert);

                switch (layout.SelectedPage)
                {
                    case BeamProPage.Connection:
                        DrawConnectionPage(layout, snapshot, connectionStatusText, operationText);
                        break;
                    case BeamProPage.Debug:
                        DrawDebugPage(layout, logs);
                        break;
                    default:
                        DrawMonitorPage(layout, snapshot, evaluation, assetText);
                        break;
                }

                DrawTabs(layout);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.contentColor = oldContentColor;
                GUI.backgroundColor = oldBackgroundColor;
                GUI.enabled = oldEnabled;
                GUI.depth = oldDepth;
            }
        }

        BeamProPageLayoutResult GetLayout(BeamProPageLayoutRequest request, string assetText,
            string connectionStatusText, string operationText, string logs, string headerContext, string headerAlert)
        {
            if (m_CachedLayout != null && SameLayoutInputs(m_LayoutRequest, request)
                && m_LayoutAssetText == assetText && m_LayoutConnectionText == connectionStatusText
                && m_LayoutOperationText == operationText && m_LayoutLogs == logs
                && m_LayoutContext == headerContext && m_LayoutAlert == headerAlert)
                return m_CachedLayout;

            var initialLayout = BeamProPageLayoutCalculator.Calculate(request);
            var monitorStatusHeight = MeasuredPanelHeight(
                assetText, initialLayout.Monitor.AssetStatus.width, 116f, 48f);
            var connectionStatusHeight = MeasuredPanelHeight(
                connectionStatusText, initialLayout.Connection.ConnectionStatus.width, 92f, 48f);
            var connectionResultHeight = MeasuredPanelHeight(
                operationText, initialLayout.Connection.OperationResult.width, 120f, 48f);
            var debugLogHeight = MeasuredPanelHeight(
                logs, initialLayout.Debug.Logs.width, 280f, 48f);
            var topStatusContentHeight = MeasuredHeaderHeight(
                headerContext,
                headerAlert,
                initialLayout.ExitButton.xMin - BeamProPageLayoutCalculator.Gap
                    - BeamProPageLayoutCalculator.OuterMargin);

            var layout = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                request.ScreenSizePixels,
                request.SafeAreaPixels,
                request.KeyboardInsetPixels,
                request.SelectedPage,
                request.EngineerMode,
                monitorStatusHeight,
                connectionStatusHeight,
                connectionResultHeight,
                debugLogHeight,
                topStatusContentHeight,
                request.DebugShowInputControl,
                request.DebugShowGestureControl,
                request.DebugShowObjectMoveControls,
                request.DebugShowPlaneControls));

            // A vertical scrollbar narrows the final content by one gutter. Re-measure wrapped text
            // against that final width so the last line cannot be clipped at the scroll threshold.
            monitorStatusHeight = MeasuredPanelHeight(
                assetText, layout.Monitor.AssetStatus.width, 116f, 48f);
            connectionStatusHeight = MeasuredPanelHeight(
                connectionStatusText, layout.Connection.ConnectionStatus.width, 92f, 48f);
            connectionResultHeight = MeasuredPanelHeight(
                operationText, layout.Connection.OperationResult.width, 120f, 48f);
            debugLogHeight = MeasuredPanelHeight(
                logs, layout.Debug.Logs.width, 280f, 48f);
            layout = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                request.ScreenSizePixels,
                request.SafeAreaPixels,
                request.KeyboardInsetPixels,
                request.SelectedPage,
                request.EngineerMode,
                monitorStatusHeight,
                connectionStatusHeight,
                connectionResultHeight,
                debugLogHeight,
                topStatusContentHeight,
                request.DebugShowInputControl,
                request.DebugShowGestureControl,
                request.DebugShowObjectMoveControls,
                request.DebugShowPlaneControls));
            m_CachedLayout = layout;
            m_LayoutRequest = request;
            m_LayoutAssetText = assetText;
            m_LayoutConnectionText = connectionStatusText;
            m_LayoutOperationText = operationText;
            m_LayoutLogs = logs;
            m_LayoutContext = headerContext;
            m_LayoutAlert = headerAlert;
            return layout;

        }

        static bool SameLayoutInputs(BeamProPageLayoutRequest a, BeamProPageLayoutRequest b)
        {
            return a.ScreenSizePixels == b.ScreenSizePixels && a.SafeAreaPixels == b.SafeAreaPixels
                && a.KeyboardInsetPixels == b.KeyboardInsetPixels && a.SelectedPage == b.SelectedPage
                && a.EngineerMode == b.EngineerMode && a.DebugShowInputControl == b.DebugShowInputControl
                && a.DebugShowGestureControl == b.DebugShowGestureControl
                && a.DebugShowObjectMoveControls == b.DebugShowObjectMoveControls
                && a.DebugShowPlaneControls == b.DebugShowPlaneControls;
        }

        float MeasureText(GUIStyle style, string text, float width)
        {
            m_MeasureContent.text = text ?? string.Empty;
            return style.CalcHeight(m_MeasureContent, width);
        }

        float MeasuredPanelHeight(string text, float width, float minimum, float chrome)
        {
            if (string.IsNullOrEmpty(text))
                return minimum;
            var contentWidth = Mathf.Max(80f, width - 32f);
            return Mathf.Max(minimum, MeasureText(m_BodyStyle, text, contentWidth) + chrome);
        }

        float MeasuredHeaderHeight(string context, string alert, float availableTextWidth)
        {
            var contentWidth = Mathf.Max(80f, availableTextWidth);
            m_HeaderContextHeight = Mathf.Max(20f,
                MeasureText(m_HeaderContextStyle, context, contentWidth));
            var alertHeight = Mathf.Max(18f,
                MeasureText(m_HeaderAlarmStyle, alert, contentWidth));
            return Mathf.Max(BeamProPageLayoutCalculator.TopStatusHeight,
                34f + m_HeaderContextHeight + alertHeight + 8f);
        }

        void DrawHeader(Rect rect, Rect exitButton, DentalNavigationSnapshot snapshot,
            DentalHudEvaluation evaluation, string context, string alert)
        {
            DrawFilledRect(rect, HeaderBackground);
            var textWidth = exitButton.xMin - BeamProPageLayoutCalculator.Gap
                - BeamProPageLayoutCalculator.OuterMargin;
            var link = DentalConnectionPresentation.Label(snapshot.Link);
            var linkColor = DentalConnectionPresentation.IndicatorColor(snapshot.Link, Green, Amber, Red);
            DrawFilledRect(new Rect(20f, 14f, 12f, 12f), linkColor);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(40f, 7f, textWidth - 20f, 28f), link, m_HeaderTitleStyle);

            var contextHeight = m_HeaderContextHeight;
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(20f, 34f, textWidth, contextHeight), context, m_HeaderContextStyle);

            GUI.contentColor = evaluation.ShowAlarm ? Red : TextSecondary;
            GUI.Label(new Rect(20f, 34f + contextHeight, textWidth,
                Mathf.Max(18f, rect.height - 34f - contextHeight - 6f)), alert, m_HeaderAlarmStyle);
            GUI.contentColor = TextPrimary;

            var oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.65f, 0.32f, 0.34f, 1f);
            if (GUI.Button(exitButton, "Exit", m_ButtonStyle))
                XREALPlugin.QuitApplication();
            GUI.backgroundColor = oldBackground;
        }

        string HeaderContext(DentalNavigationSnapshot snapshot)
        {
            if (m_HeaderContext != null && m_HeaderHasContext == snapshot.HasContext
                && m_HeaderTooth == snapshot.ToothId && m_HeaderPlan == snapshot.PlanId)
                return m_HeaderContext;
            m_HeaderHasContext = snapshot.HasContext;
            m_HeaderTooth = snapshot.ToothId;
            m_HeaderPlan = snapshot.PlanId;
            m_HeaderContext = snapshot.HasContext
                ? $"Tooth {snapshot.ToothId} · Plan {snapshot.PlanId}"
                : "Waiting for navigation context";
            return m_HeaderContext;
        }

        string HeaderAlert(DentalHudEvaluation evaluation)
        {
            if (evaluation.ShowAlarm && !string.IsNullOrEmpty(evaluation.AlarmText))
                return evaluation.AlarmText;
            return m_RobotDisplay != null
                ? UserFacingStatus(m_RobotDisplay.ConnectionStatus)
                : string.Empty;
        }

        void DrawMonitorPage(
            BeamProPageLayoutResult layout,
            DentalNavigationSnapshot snapshot,
            DentalHudEvaluation evaluation,
            string assetText)
        {
            var page = layout.Monitor;
            m_MonitorScroll = GUI.BeginScrollView(
                layout.ContentViewport,
                m_MonitorScroll,
                page.ScrollContent,
                false,
                page.ScrollContent.height > layout.ContentViewport.height);

            DrawLeftEyePreview(page.Preview);
            DrawMetricCard(page.MetricCards[0], "Remaining depth",
                DepthValue(snapshot, evaluation),
                DepthDetail(snapshot, evaluation),
                evaluation.Depth,
                false);
            DrawMetricCard(page.MetricCards[1], "Position offset",
                evaluation.DashNumbers ? "—" : $"{snapshot.LateralMm:0.0} mm",
                evaluation.DashNumbers
                    ? "Data unavailable"
                    : snapshot.HasLateralDirection
                        ? DentalNavigationPresentation.DirectionPhrase(snapshot.LateralBuccalMm, snapshot.LateralMesialMm, "toward ")
                        : "Direction unavailable",
                evaluation.Lateral,
                true);
            DrawMetricCard(page.MetricCards[2], "Angle deviation",
                evaluation.DashNumbers ? "—" : $"{snapshot.AngleDeg:0.0}°",
                evaluation.DashNumbers
                    ? "Data unavailable"
                    : snapshot.HasTiltDirection
                        ? DentalNavigationPresentation.DirectionPhrase(snapshot.TiltBuccalDeg, snapshot.TiltMesialDeg, "tilt toward ")
                        : "Direction unavailable",
                evaluation.Angle,
                true);
            DrawMetricCard(page.MetricCards[3], "Overall status",
                DentalNavigationPresentation.BeamProOverallPhrase(evaluation.Overall),
                evaluation.ShowAlarm ? "See alert above" : "Each metric evaluated separately",
                evaluation.Overall,
                true);

            var hudVisible = m_Owner == null || m_Owner.HudVisible;
            if (GUI.Button(page.HudToggle, hudVisible ? "HUD: visible" : "HUD: hidden", m_ButtonStyle)
                && m_Owner != null)
            {
                m_Owner.SetHudVisibleFromBeamPro(!hudVisible);
            }

            var modelVisible = m_Owner != null && m_Owner.ModelVisible;
            if (GUI.Button(page.ModelToggle, modelVisible ? "3D model: visible" : "3D model: hidden", m_ButtonStyle)
                && m_Owner != null)
            {
                m_Owner.SetModelVisibleFromBeamPro(!modelVisible);
            }

            var followsHead = m_Owner == null || m_Owner.ContentFollowsHead;
            DrawAnchorModeButton(
                page.HoverButton,
                followsHead ? "Hover" : "Hover (active)",
                !followsHead,
                DentalContentAnchorMode.WorldLocked);
            DrawAnchorModeButton(
                page.FollowButton,
                followsHead ? "Follow (active)" : "Follow",
                followsHead,
                DentalContentAnchorMode.FollowHead);

            DrawPanel(page.AssetStatus);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.AssetStatus.x + 16f, page.AssetStatus.y + 10f,
                page.AssetStatus.width - 32f, 26f), "Case and asset status", m_SectionTitleStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(page.AssetStatus.x + 16f, page.AssetStatus.y + 38f,
                page.AssetStatus.width - 32f, page.AssetStatus.height - 46f), assetText, m_BodyStyle);
            GUI.EndScrollView();
            GUI.contentColor = TextPrimary;
        }

        void DrawLeftEyePreview(Rect rect)
        {
            DrawFilledRect(rect, PreviewBackground);
            if (m_LeftEye != null && m_LeftEye.TryGetLiveXrFrame(out var texture))
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
                GUI.contentColor = TextPrimary;
                GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 24f),
                    "XR left eye · Live", m_CaptionStyle);
                if (!string.IsNullOrEmpty(m_LeftEye.PreviewDebugInfo))
                {
                    GUI.contentColor = TextSecondary;
                    GUI.Label(new Rect(rect.x + 12f, rect.yMax - 30f, rect.width - 24f, 20f),
                        m_LeftEye.PreviewDebugInfo, m_CaptionStyle);
                }
                return;
            }

            var message = m_LeftEye == null
                ? "Glasses preview component unavailable"
                : string.IsNullOrEmpty(m_LeftEye.PreviewStatusMessage)
                    ? "Waiting for a live XR left-eye frame"
                    : m_LeftEye.PreviewStatusMessage;
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(rect.x + 20f, rect.y + 20f, rect.width - 40f, rect.height - 40f),
                message, m_BodyStyle);
            GUI.contentColor = TextPrimary;
        }

        void DrawMetricCard(
            Rect rect,
            string title,
            string value,
            string detail,
            DentalMetricGrade grade,
            bool allowGreen)
        {
            DrawPanel(rect);
            GUI.contentColor = TextSecondary;
            DrawFittedMetricLabel(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), title, m_CaptionStyle);
            GUI.contentColor = GradeColor(grade, allowGreen);
            DrawFittedMetricLabel(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 44f), value, m_ValueStyle);
            GUI.contentColor = TextSecondary;
            DrawFittedMetricLabel(new Rect(rect.x + 12f, rect.y + 76f, rect.width - 24f, rect.height - 82f),
                detail, m_CaptionStyle);
            GUI.contentColor = TextPrimary;
        }

        void DrawFittedMetricLabel(Rect rect, string text, GUIStyle style)
        {
            var originalSize = style.fontSize;
            m_MeasureContent.text = text;
            if (!style.wordWrap)
            {
                var width = style.CalcSize(m_MeasureContent).x;
                if (width > rect.width)
                    style.fontSize = Mathf.Max(12, Mathf.FloorToInt(originalSize * rect.width / width));
            }
            while (style.fontSize > 12 && style.CalcHeight(m_MeasureContent, rect.width) > rect.height)
                style.fontSize--;
            GUI.Label(rect, m_MeasureContent, style);
            style.fontSize = originalSize;
        }

        void DrawConnectionPage(
            BeamProPageLayoutResult layout,
            DentalNavigationSnapshot snapshot,
            string statusText,
            string operationText)
        {
            var page = layout.Connection;
            m_ConnectionScroll = GUI.BeginScrollView(
                layout.ContentViewport,
                m_ConnectionScroll,
                page.ScrollContent,
                false,
                page.ScrollContent.height > layout.ContentViewport.height);

            DrawPanel(page.ConnectionStatus);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.ConnectionStatus.x + 16f, page.ConnectionStatus.y + 10f,
                page.ConnectionStatus.width - 32f, 26f), "Connection status", m_SectionTitleStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(page.ConnectionStatus.x + 16f, page.ConnectionStatus.y + 39f,
                page.ConnectionStatus.width - 32f, page.ConnectionStatus.height - 47f), statusText, m_BodyStyle);

            DrawInputBlock(page.IpInput, "Navigation software IPv4 address", HostControlName, ref m_EditableHost, 64);
            DrawInputBlock(page.PortInput, "Port", PortControlName, ref m_EditablePort, 5);

            var connecting = m_ConnectionRequestInFlight || snapshot.Link == DentalLinkState.Connecting;
            var previousEnabled = GUI.enabled;
            GUI.enabled = !connecting && m_RobotDisplay != null;
            var buttonLabel = connecting
                ? "Connecting…"
                : snapshot.Link == DentalLinkState.Live ? "Reconnect" : "Connect";
            if (GUI.Button(page.ConnectButton, buttonLabel, m_ButtonStyle))
                StartConnectionRequest();
            GUI.enabled = previousEnabled;

            DrawPanel(page.OperationResult);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.OperationResult.x + 16f, page.OperationResult.y + 10f,
                page.OperationResult.width - 32f, 26f), "Operation result", m_SectionTitleStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(page.OperationResult.x + 16f, page.OperationResult.y + 39f,
                page.OperationResult.width - 32f, page.OperationResult.height - 47f), operationText, m_BodyStyle);

            var focused = GUI.GetNameOfFocusedControl();
            if (TouchScreenKeyboard.visible)
            {
                var target = focused == HostControlName
                    ? page.IpInput
                    : focused == PortControlName ? page.PortInput : page.ConnectButton;
                m_ConnectionScroll.y = BeamProPageLayoutCalculator.ComputeScrollOffsetToReveal(
                    layout.ContentViewport.height,
                    page.ScrollContent.height,
                    target,
                    m_ConnectionScroll.y);
            }

            GUI.EndScrollView();
            GUI.contentColor = TextPrimary;
        }

        void DrawInputBlock(Rect rect, string label, string controlName, ref string value, int maxLength)
        {
            DrawPanel(rect);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 5f, rect.width - 24f, 22f), label, m_CaptionStyle);
            GUI.SetNextControlName(controlName);
            value = GUI.TextField(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 56f),
                value ?? string.Empty, maxLength, m_FieldStyle);
        }

        void StartConnectionRequest()
        {
            if (m_ConnectionRequestInFlight || m_RobotDisplay == null)
                return;

            if (m_RobotDisplay.TryStartEndpointSearch(m_EditableHost, m_EditablePort, out var result))
            {
                m_ConnectionRequestInFlight = true;
                var state = DentalNavigationState.Instance;
                m_ConnectionInitialLink = state != null
                    ? state.Capture(Time.realtimeSinceStartup).Link
                    : DentalLinkState.Idle;
                m_ConnectionSawConnecting = m_ConnectionInitialLink == DentalLinkState.Connecting;
                m_ConnectionPendingStatus = result;
                m_ConnectionRequestStartedRealtime = Time.realtimeSinceStartup;
            }
            m_ConnectionResult = result;
        }

        void DrawDebugPage(BeamProPageLayoutResult layout, string logs)
        {
            var page = layout.Debug;
            m_DebugScroll = GUI.BeginScrollView(
                layout.ContentViewport,
                m_DebugScroll,
                page.ScrollContent,
                false,
                page.ScrollContent.height > layout.ContentViewport.height);

            DrawDebugGroup(page.DisplayControls, "Display controls");
            DrawPairButtons(page.ControlRows[0],
                m_Owner != null && m_Owner.HudVisible ? "Hide HUD" : "Show HUD",
                () => m_Owner?.SetHudVisibleFromBeamPro(!(m_Owner?.HudVisible ?? true)),
                m_Owner != null && m_Owner.ModelVisible ? "Hide 3D model" : "Show 3D model",
                () => m_Owner?.SetModelVisibleFromBeamPro(!(m_Owner?.ModelVisible ?? false)),
                m_Owner != null,
                "Display controls unavailable");
            DrawSingleButton(page.ControlRows[1],
                m_Owner != null && m_Owner.GlassesControlWindowVisible ? "Hide glasses controls" : "Show glasses controls",
                () => m_Owner?.ToggleGlassesControlWindow(),
                m_Owner != null);
            if (HasArea(page.ControlRows[2]))
            {
                DrawSingleButton(page.ControlRows[2],
                    m_Owner != null && m_Owner.IsHandInput ? "Use Controller" : "Use Hands",
                    () => m_Owner?.ToggleBeamProInputSource(),
                    m_Owner != null);
            }

            DrawDebugGroup(page.CameraAndGestureControls, "Camera and gestures");
            DrawSingleButton(page.ControlRows[3],
                m_Owner != null && m_Owner.LocalRgbPreviewVisible ? "Hide local RGB preview" : "Show local RGB preview",
                () => m_Owner?.ToggleLocalRgbPreview(),
                RgbFeaturePolicy.Enabled && m_Owner != null && m_Owner.HasLocalRgbPreview,
                RgbFeaturePolicy.Enabled ? "RGB preview unavailable" : RgbFeaturePolicy.DisabledMessage);
            if (HasArea(page.ControlRows[4]))
            {
                DrawSingleButton(page.ControlRows[4],
                    m_Owner != null && m_Owner.GestureRecognitionEnabled ? "Disable offline gestures" : "Enable offline gestures",
                    () => m_Owner?.ToggleGestureRecognition(),
                    RgbFeaturePolicy.Enabled && m_Owner != null,
                    RgbFeaturePolicy.Enabled ? "Gesture recognition unavailable" : RgbFeaturePolicy.DisabledMessage);
            }

            if (HasArea(page.NavigationAndMediaControls))
                DrawDebugGroup(page.NavigationAndMediaControls, "Navigation objects and check plane");
            var canMove = m_Owner != null && m_Owner.ShowBeamProObjectMoveButtons && m_Owner.HasReferenceTargets;
            if (HasArea(page.ControlRows[5]))
                DrawPairButtons(page.ControlRows[5], "X +", () => m_Owner?.MoveReferenceTargets(Vector3.right),
                    "X −", () => m_Owner?.MoveReferenceTargets(Vector3.left), canMove, "Object movement unavailable");
            if (HasArea(page.ControlRows[6]))
                DrawPairButtons(page.ControlRows[6], "Y +", () => m_Owner?.MoveReferenceTargets(Vector3.up),
                    "Y −", () => m_Owner?.MoveReferenceTargets(Vector3.down), canMove, "Object movement unavailable");
            if (HasArea(page.ControlRows[7]))
                DrawPairButtons(page.ControlRows[7], "Z +", () => m_Owner?.MoveReferenceTargets(Vector3.forward),
                    "Z −", () => m_Owner?.MoveReferenceTargets(Vector3.back), canMove, "Object movement unavailable");

            var canAdjustPlane = m_Owner != null
                && m_Owner.ShowBeamProCheckPlaneAppearanceButtons
                && m_Owner.HasCheckPlane;
            if (HasArea(page.ControlRows[8]))
                DrawPairButtons(page.ControlRows[8], "Transparency +10%", () => m_Owner?.AdjustCheckPlaneTransparency(true),
                    "Transparency −10%", () => m_Owner?.AdjustCheckPlaneTransparency(false), canAdjustPlane,
                    "Check plane unavailable");
            if (HasArea(page.ControlRows[9]))
                DrawPairButtons(page.ControlRows[9], "Red +", () => m_Owner?.AdjustCheckPlaneColor(0, 25),
                    "Red −", () => m_Owner?.AdjustCheckPlaneColor(0, -25), canAdjustPlane, "Check plane unavailable");
            if (HasArea(page.ControlRows[10]))
                DrawPairButtons(page.ControlRows[10], "Green +", () => m_Owner?.AdjustCheckPlaneColor(1, 25),
                    "Green −", () => m_Owner?.AdjustCheckPlaneColor(1, -25), canAdjustPlane, "Check plane unavailable");
            if (HasArea(page.ControlRows[11]))
                DrawPairButtons(page.ControlRows[11], "Blue +", () => m_Owner?.AdjustCheckPlaneColor(2, 25),
                    "Blue −", () => m_Owner?.AdjustCheckPlaneColor(2, -25), canAdjustPlane, "Check plane unavailable");

            DrawPanel(page.Logs);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.Logs.x + 16f, page.Logs.y + 10f, page.Logs.width - 32f, 26f),
                "Runtime log", m_SectionTitleStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(page.Logs.x + 16f, page.Logs.y + 39f,
                page.Logs.width - 32f, page.Logs.height - 47f), logs, m_BodyStyle);
            GUI.EndScrollView();
            GUI.contentColor = TextPrimary;
        }

        void DrawDebugGroup(Rect rect, string title)
        {
            DrawPanel(rect);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, 30f), title, m_SectionTitleStyle);
        }

        static bool HasArea(Rect rect)
        {
            return rect.width > 0f && rect.height > 0f;
        }

        void DrawSingleButton(Rect rect, string label, System.Action action, bool enabled, string unavailable = "Unavailable")
        {
            var previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            if (GUI.Button(rect, enabled ? label : unavailable, m_ButtonStyle) && enabled)
                action?.Invoke();
            GUI.enabled = previousEnabled;
        }

        void DrawPairButtons(
            Rect rect,
            string leftLabel,
            System.Action leftAction,
            string rightLabel,
            System.Action rightAction,
            bool enabled = true,
            string unavailable = "Unavailable")
        {
            if (!enabled && !string.IsNullOrEmpty(unavailable))
            {
                DrawSingleButton(rect, unavailable, null, false, unavailable);
                return;
            }

            if (!enabled)
                return;

            var halfWidth = (rect.width - BeamProPageLayoutCalculator.Gap) * 0.5f;
            var left = new Rect(rect.x, rect.y, halfWidth, rect.height);
            var right = new Rect(rect.x + halfWidth + BeamProPageLayoutCalculator.Gap,
                rect.y, halfWidth, rect.height);
            if (GUI.Button(left, leftLabel, m_ButtonStyle))
                leftAction?.Invoke();
            if (GUI.Button(right, rightLabel, m_ButtonStyle))
                rightAction?.Invoke();
        }

        void DrawAnchorModeButton(
            Rect rect,
            string label,
            bool selected,
            DentalContentAnchorMode mode)
        {
            var previousEnabled = GUI.enabled;
            var previousBackground = GUI.backgroundColor;
            GUI.enabled = m_Owner != null;
            GUI.backgroundColor = selected ? Blue : Color.white;
            if (GUI.Button(rect, label, m_ButtonStyle) && m_Owner != null && !selected)
                m_Owner.SetContentAnchorModeFromBeamPro(mode);
            GUI.backgroundColor = previousBackground;
            GUI.enabled = previousEnabled;
        }

        void DrawTabs(BeamProPageLayoutResult layout)
        {
            DrawFilledRect(layout.BottomTabBar, HeaderBackground);
            for (var i = 0; i < layout.TabButtons.Length && i < TabLabels.Length; i++)
            {
                var page = (BeamProPage)i;
                var selected = page == layout.SelectedPage;
                var oldBackground = GUI.backgroundColor;
                GUI.backgroundColor = selected ? Blue : Color.white;
                if (GUI.Button(layout.TabButtons[i], TabLabels[i], m_TabStyle) && !selected)
                {
                    m_SelectedPage = page;
                    GUI.FocusControl(string.Empty);
                }
                GUI.backgroundColor = oldBackground;
            }
        }

        string BuildAssetStatus(DentalNavigationSnapshot snapshot)
        {
            var ct = DentalCtVolumeService.Instance != null
                ? DentalCtVolumeService.Instance.StatusMessage
                : "CT not loaded";
            var transfer = m_RobotDisplay != null
                ? UserFacingStatus(m_RobotDisplay.TransferSummary)
                : "Asset service unavailable";
            if (m_AssetText != null && m_AssetSnapshot.HasContext == snapshot.HasContext
                && m_AssetSnapshot.CaseId == snapshot.CaseId && m_AssetSnapshot.ToothId == snapshot.ToothId
                && m_AssetSnapshot.PlanId == snapshot.PlanId && m_AssetSnapshot.ToolId == snapshot.ToolId
                && m_AssetSnapshot.HasThresholds == snapshot.HasThresholds
                && m_AssetSnapshot.Thresholds.ConfigVersion == snapshot.Thresholds.ConfigVersion
                && m_AssetCtStatus == ct && m_AssetTransfer == transfer)
                return m_AssetText;
            m_AssetSnapshot = snapshot;
            m_AssetCtStatus = ct;
            m_AssetTransfer = transfer;
            var thresholds = snapshot.HasThresholds
                ? $"Thresholds v{snapshot.Thresholds.ConfigVersion}" : "Thresholds not synced";
            var context = snapshot.HasContext
                ? $"Case {snapshot.CaseId} · Tooth {snapshot.ToothId} · Plan {snapshot.PlanId} · Drill {snapshot.ToolId}"
                : "Case context not synced";
            m_AssetText = $"{context}\n{thresholds} · {ct}\n{transfer}";
            return m_AssetText;
        }

        string BuildConnectionStatus(DentalNavigationSnapshot snapshot)
        {
            var endpoint = m_RobotDisplay != null ? m_RobotDisplay.ServerAddress : "—";
            var detail = m_RobotDisplay != null
                ? UserFacingStatus(m_RobotDisplay.ConnectionStatus)
                : "Connection service unavailable";
            if (m_ConnectionText != null && m_ConnectionLink == snapshot.Link
                && m_ConnectionEndpoint == endpoint && m_ConnectionDetail == detail)
                return m_ConnectionText;
            m_ConnectionLink = snapshot.Link;
            m_ConnectionEndpoint = endpoint;
            m_ConnectionDetail = detail;
            m_ConnectionText = $"{DentalConnectionPresentation.Label(snapshot.Link)}\nCurrent address: {endpoint}\n{detail}";
            return m_ConnectionText;
        }

        static string UserFacingStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Status unavailable";
            if (status.IndexOf("PlatformNotSupportedException", System.StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("gRPC requires extra configuration", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "gRPC HTTP/2 transport initialization failed. Check that the app includes the HTTP/2 handler and native dependencies for this platform.";
            }
            return status.Trim();
        }

        static string DepthValue(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (evaluation.DashNumbers || !snapshot.HasDepthBreakdown)
                return "—";
            return snapshot.RemainingDepthMm < 0f
                ? $"Over depth {Mathf.Abs(snapshot.RemainingDepthMm):0.0} mm"
                : $"{snapshot.RemainingDepthMm:0.0} mm";
        }

        static string DepthDetail(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (evaluation.DashNumbers || !snapshot.HasDepthBreakdown)
                return "Current / target unavailable";
            return $"Current {snapshot.CurrentDepthMm:0.0} · Target {snapshot.TargetDepthMm:0.0} mm";
        }

        static Color GradeColor(DentalMetricGrade grade, bool allowGreen)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green:
                    return allowGreen ? Green : TextPrimary;
                case DentalMetricGrade.Amber:
                    return Amber;
                case DentalMetricGrade.Red:
                    return Red;
                case DentalMetricGrade.Stale:
                    return Amber;
                default:
                    return Disabled;
            }
        }

        void DrawPanel(Rect rect)
        {
            var previous = GUI.color;
            GUI.color = PanelBackground;
            GUI.Box(rect, GUIContent.none, m_PanelStyle);
            GUI.color = previous;
        }

        static void DrawFilledRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void EnsureStyles()
        {
            if (m_BodyStyle != null)
                return;

            m_HeaderTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextPrimary }
            };
            m_HeaderContextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = TextSecondary }
            };
            m_HeaderAlarmStyle = new GUIStyle(m_HeaderContextStyle)
            {
                fontSize = 16
            };
            m_SectionTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextPrimary }
            };
            m_BodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = TextSecondary }
            };
            m_CaptionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = TextSecondary }
            };
            m_ValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = TextPrimary }
            };
            m_ButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = TextPrimary }
            };
            m_TabStyle = new GUIStyle(m_ButtonStyle)
            {
                fontSize = 20
            };
            m_FieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8)
            };
            m_PanelStyle = new GUIStyle(GUI.skin.box);
        }
    }
}
