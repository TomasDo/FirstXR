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
        string m_ConnectionResult = "尚未发起手动连接。";
        bool m_DraftsInitialized;
        bool m_ConnectionRequestInFlight;
        bool m_ConnectionSawConnecting;
        DentalLinkState m_ConnectionInitialLink;
        string m_ConnectionPendingStatus;
        float m_ConnectionRequestStartedRealtime;
        float m_NextDependencyRefreshRealtime;

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
                FinishConnectionRequest($"已连接 {m_RobotDisplay?.ServerAddress ?? string.Empty}".TrimEnd());
            }
            else if (link == DentalLinkState.Lost
                     && (m_ConnectionSawConnecting
                         || m_ConnectionInitialLink != DentalLinkState.Lost
                         || statusChanged))
            {
                FinishConnectionRequest(m_RobotDisplay != null
                    ? UserFacingStatus(m_RobotDisplay.ConnectionStatus)
                    : "连接失败，请检查导航软件和局域网。");
            }
            else if (elapsed >= ConnectionAttemptTimeoutSeconds)
            {
                FinishConnectionRequest("连接超时，请检查 IP、端口和导航软件状态。");
            }
        }

        void FinishConnectionRequest(string result)
        {
            m_ConnectionRequestInFlight = false;
            m_ConnectionSawConnecting = false;
            m_ConnectionPendingStatus = null;
            m_ConnectionResult = string.IsNullOrEmpty(result) ? "连接已结束。" : result;
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
            var initialLayout = BeamProPageLayoutCalculator.Calculate(initialRequest);
            m_SelectedPage = initialLayout.SelectedPage;

            var assetText = BuildAssetStatus(snapshot);
            var connectionStatusText = BuildConnectionStatus(snapshot);
            var operationText = string.IsNullOrEmpty(m_ConnectionResult)
                ? "尚未发起手动连接。"
                : m_ConnectionResult;
            var logs = BeamProUnifiedLogWindow.SnapshotText;
            var headerContext = HeaderContext(snapshot);
            var headerAlert = HeaderAlert(evaluation);
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
                initialLayout.TopStatusBar.width);

            var layout = BeamProPageLayoutCalculator.Calculate(new BeamProPageLayoutRequest(
                new Vector2(Screen.width, Screen.height),
                safeArea,
                keyboardInset,
                m_SelectedPage,
                m_EngineerMode,
                monitorStatusHeight,
                connectionStatusHeight,
                connectionResultHeight,
                debugLogHeight,
                topStatusContentHeight,
                debugShowInput,
                debugShowGesture,
                debugShowMove,
                debugShowPlane));

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
                new Vector2(Screen.width, Screen.height),
                safeArea,
                keyboardInset,
                m_SelectedPage,
                m_EngineerMode,
                monitorStatusHeight,
                connectionStatusHeight,
                connectionResultHeight,
                debugLogHeight,
                topStatusContentHeight,
                debugShowInput,
                debugShowGesture,
                debugShowMove,
                debugShowPlane));
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
                DrawHeader(layout.TopStatusBar, snapshot, evaluation);

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

        float MeasuredPanelHeight(string text, float width, float minimum, float chrome)
        {
            var contentWidth = Mathf.Max(80f, width - 32f);
            return Mathf.Max(minimum, m_BodyStyle.CalcHeight(new GUIContent(text ?? string.Empty), contentWidth) + chrome);
        }

        float MeasuredHeaderHeight(string context, string alert, float width)
        {
            var contentWidth = Mathf.Max(80f, width - 40f);
            var contextHeight = Mathf.Max(20f,
                m_HeaderContextStyle.CalcHeight(new GUIContent(context ?? string.Empty), contentWidth));
            var alertHeight = Mathf.Max(18f,
                m_HeaderAlarmStyle.CalcHeight(new GUIContent(alert ?? string.Empty), contentWidth));
            return Mathf.Max(BeamProPageLayoutCalculator.TopStatusHeight,
                34f + contextHeight + alertHeight + 8f);
        }

        void DrawHeader(Rect rect, DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            DrawFilledRect(rect, HeaderBackground);
            var link = LinkPhrase(snapshot, evaluation);
            var linkColor = LinkColor(snapshot, evaluation);
            DrawFilledRect(new Rect(20f, 14f, 12f, 12f), linkColor);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(40f, 7f, rect.width - 60f, 28f), link, m_HeaderTitleStyle);

            var context = HeaderContext(snapshot);
            var contextHeight = Mathf.Max(20f,
                m_HeaderContextStyle.CalcHeight(new GUIContent(context), rect.width - 40f));
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(20f, 34f, rect.width - 40f, contextHeight), context, m_HeaderContextStyle);

            var alert = HeaderAlert(evaluation);
            GUI.contentColor = evaluation.ShowAlarm ? Red : TextSecondary;
            GUI.Label(new Rect(20f, 34f + contextHeight, rect.width - 40f,
                Mathf.Max(18f, rect.height - 34f - contextHeight - 6f)), alert, m_HeaderAlarmStyle);
            GUI.contentColor = TextPrimary;
        }

        static string HeaderContext(DentalNavigationSnapshot snapshot)
        {
            return snapshot.HasContext
                ? $"牙位 {snapshot.ToothId} · 规划 {snapshot.PlanId}"
                : "等待导航上下文";
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
            DrawMetricCard(page.MetricCards[0], "剩余深度",
                DepthValue(snapshot, evaluation),
                DepthDetail(snapshot, evaluation),
                evaluation.Depth,
                false);
            DrawMetricCard(page.MetricCards[1], "位置偏移",
                evaluation.DashNumbers ? "—" : $"{snapshot.LateralMm:0.0} mm",
                evaluation.DashNumbers
                    ? "数据不可用"
                    : snapshot.HasLateralDirection
                        ? DirectionPhrase(snapshot.LateralBuccalMm, snapshot.LateralMesialMm, "偏")
                        : "方向数据缺失",
                evaluation.Lateral,
                true);
            DrawMetricCard(page.MetricCards[2], "角度偏差",
                evaluation.DashNumbers ? "—" : $"{snapshot.AngleDeg:0.0}°",
                evaluation.DashNumbers
                    ? "数据不可用"
                    : snapshot.HasTiltDirection
                        ? DirectionPhrase(snapshot.TiltBuccalDeg, snapshot.TiltMesialDeg, "向", "倾斜")
                        : "方向数据缺失",
                evaluation.Angle,
                true);
            DrawMetricCard(page.MetricCards[3], "综合状态",
                OverallPhrase(evaluation.Overall),
                evaluation.ShowAlarm ? evaluation.AlarmText : "各项独立判定",
                evaluation.Overall,
                true);

            var hudVisible = m_Owner == null || m_Owner.HudVisible;
            if (GUI.Button(page.HudToggle, hudVisible ? "HUD：显示中" : "HUD：已隐藏", m_ButtonStyle)
                && m_Owner != null)
            {
                m_Owner.SetHudVisibleFromBeamPro(!hudVisible);
            }

            var modelVisible = m_Owner != null && m_Owner.ModelVisible;
            if (GUI.Button(page.ModelToggle, modelVisible ? "三维模型：显示中" : "三维模型：已隐藏", m_ButtonStyle)
                && m_Owner != null)
            {
                m_Owner.SetModelVisibleFromBeamPro(!modelVisible);
            }

            DrawPanel(page.AssetStatus);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.AssetStatus.x + 16f, page.AssetStatus.y + 10f,
                page.AssetStatus.width - 32f, 26f), "病例与资产状态", m_SectionTitleStyle);
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
                    "眼镜左眼 · 实时", m_CaptionStyle);
                if (!string.IsNullOrEmpty(m_LeftEye.PreviewDebugInfo))
                {
                    GUI.contentColor = TextSecondary;
                    GUI.Label(new Rect(rect.x + 12f, rect.yMax - 30f, rect.width - 24f, 20f),
                        m_LeftEye.PreviewDebugInfo, m_CaptionStyle);
                }
                return;
            }

            var message = m_LeftEye == null
                ? "眼镜预览组件不可用"
                : string.IsNullOrEmpty(m_LeftEye.PreviewStatusMessage)
                    ? "等待真实 XR 左眼画面"
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
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), title, m_CaptionStyle);
            GUI.contentColor = GradeColor(grade, allowGreen);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 44f), value, m_ValueStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 76f, rect.width - 24f, rect.height - 82f),
                detail, m_CaptionStyle);
            GUI.contentColor = TextPrimary;
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
                page.ConnectionStatus.width - 32f, 26f), "连接状态", m_SectionTitleStyle);
            GUI.contentColor = TextSecondary;
            GUI.Label(new Rect(page.ConnectionStatus.x + 16f, page.ConnectionStatus.y + 39f,
                page.ConnectionStatus.width - 32f, page.ConnectionStatus.height - 47f), statusText, m_BodyStyle);

            DrawInputBlock(page.IpInput, "导航软件 IPv4 地址", HostControlName, ref m_EditableHost, 64);
            DrawInputBlock(page.PortInput, "端口", PortControlName, ref m_EditablePort, 5);

            var connecting = m_ConnectionRequestInFlight || snapshot.Link == DentalLinkState.Connecting;
            var previousEnabled = GUI.enabled;
            GUI.enabled = !connecting && m_RobotDisplay != null;
            var buttonLabel = connecting
                ? "正在连接…"
                : snapshot.Link == DentalLinkState.Live ? "重新连接" : "连接";
            if (GUI.Button(page.ConnectButton, buttonLabel, m_ButtonStyle))
                StartConnectionRequest();
            GUI.enabled = previousEnabled;

            DrawPanel(page.OperationResult);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.OperationResult.x + 16f, page.OperationResult.y + 10f,
                page.OperationResult.width - 32f, 26f), "操作结果", m_SectionTitleStyle);
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

            DrawDebugGroup(page.DisplayControls, "显示控制");
            DrawPairButtons(page.ControlRows[0],
                m_Owner != null && m_Owner.HudVisible ? "隐藏 HUD" : "显示 HUD",
                () => m_Owner?.SetHudVisibleFromBeamPro(!(m_Owner?.HudVisible ?? true)),
                m_Owner != null && m_Owner.ModelVisible ? "隐藏三维模型" : "显示三维模型",
                () => m_Owner?.SetModelVisibleFromBeamPro(!(m_Owner?.ModelVisible ?? false)),
                m_Owner != null,
                "显示控制不可用");
            DrawSingleButton(page.ControlRows[1],
                m_Owner != null && m_Owner.GlassesControlWindowVisible ? "隐藏眼镜控制界面" : "显示眼镜控制界面",
                () => m_Owner?.ToggleGlassesControlWindow(),
                m_Owner != null);
            if (HasArea(page.ControlRows[2]))
            {
                DrawSingleButton(page.ControlRows[2],
                    m_Owner != null && m_Owner.IsHandInput ? "切换到 Controller" : "切换到 Hand",
                    () => m_Owner?.ToggleBeamProInputSource(),
                    m_Owner != null);
            }

            DrawDebugGroup(page.CameraAndGestureControls, "相机与手势");
            DrawSingleButton(page.ControlRows[3],
                m_Owner != null && m_Owner.LocalRgbPreviewVisible ? "关闭本地 RGB 预览" : "打开本地 RGB 预览",
                () => m_Owner?.ToggleLocalRgbPreview(),
                m_Owner != null && m_Owner.HasLocalRgbPreview);
            if (HasArea(page.ControlRows[4]))
            {
                DrawSingleButton(page.ControlRows[4],
                    m_Owner != null && m_Owner.GestureRecognitionEnabled ? "关闭离线手势识别" : "开启离线手势识别",
                    () => m_Owner?.ToggleGestureRecognition(),
                    m_Owner != null);
            }

            if (HasArea(page.NavigationAndMediaControls))
                DrawDebugGroup(page.NavigationAndMediaControls, "导航对象与检查平面");
            var canMove = m_Owner != null && m_Owner.ShowBeamProObjectMoveButtons && m_Owner.HasReferenceTargets;
            if (HasArea(page.ControlRows[5]))
                DrawPairButtons(page.ControlRows[5], "X +", () => m_Owner?.MoveReferenceTargets(Vector3.right),
                    "X −", () => m_Owner?.MoveReferenceTargets(Vector3.left), canMove, "对象移动不可用");
            if (HasArea(page.ControlRows[6]))
                DrawPairButtons(page.ControlRows[6], "Y +", () => m_Owner?.MoveReferenceTargets(Vector3.up),
                    "Y −", () => m_Owner?.MoveReferenceTargets(Vector3.down), canMove, "对象移动不可用");
            if (HasArea(page.ControlRows[7]))
                DrawPairButtons(page.ControlRows[7], "Z +", () => m_Owner?.MoveReferenceTargets(Vector3.forward),
                    "Z −", () => m_Owner?.MoveReferenceTargets(Vector3.back), canMove, "对象移动不可用");

            var canAdjustPlane = m_Owner != null
                && m_Owner.ShowBeamProCheckPlaneAppearanceButtons
                && m_Owner.HasCheckPlane;
            if (HasArea(page.ControlRows[8]))
                DrawPairButtons(page.ControlRows[8], "透明度 +10%", () => m_Owner?.AdjustCheckPlaneTransparency(true),
                    "透明度 −10%", () => m_Owner?.AdjustCheckPlaneTransparency(false), canAdjustPlane,
                    "检查平面不可用");
            if (HasArea(page.ControlRows[9]))
                DrawPairButtons(page.ControlRows[9], "红色 +", () => m_Owner?.AdjustCheckPlaneColor(0, 25),
                    "红色 −", () => m_Owner?.AdjustCheckPlaneColor(0, -25), canAdjustPlane, "检查平面不可用");
            if (HasArea(page.ControlRows[10]))
                DrawPairButtons(page.ControlRows[10], "绿色 +", () => m_Owner?.AdjustCheckPlaneColor(1, 25),
                    "绿色 −", () => m_Owner?.AdjustCheckPlaneColor(1, -25), canAdjustPlane, "检查平面不可用");
            if (HasArea(page.ControlRows[11]))
                DrawPairButtons(page.ControlRows[11], "蓝色 +", () => m_Owner?.AdjustCheckPlaneColor(2, 25),
                    "蓝色 −", () => m_Owner?.AdjustCheckPlaneColor(2, -25), canAdjustPlane, "检查平面不可用");

            DrawPanel(page.Logs);
            GUI.contentColor = TextPrimary;
            GUI.Label(new Rect(page.Logs.x + 16f, page.Logs.y + 10f, page.Logs.width - 32f, 26f),
                "运行日志", m_SectionTitleStyle);
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

        void DrawSingleButton(Rect rect, string label, System.Action action, bool enabled, string unavailable = "不可用")
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
            string unavailable = "不可用")
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

        void DrawTabs(BeamProPageLayoutResult layout)
        {
            DrawFilledRect(layout.BottomTabBar, HeaderBackground);
            var pages = m_EngineerMode
                ? new[] { BeamProPage.Monitor, BeamProPage.Connection, BeamProPage.Debug }
                : new[] { BeamProPage.Monitor, BeamProPage.Connection };
            var labels = m_EngineerMode
                ? new[] { "监看", "连接", "调试" }
                : new[] { "监看", "连接" };

            for (var i = 0; i < layout.TabButtons.Length && i < pages.Length; i++)
            {
                var selected = pages[i] == layout.SelectedPage;
                var oldBackground = GUI.backgroundColor;
                GUI.backgroundColor = selected ? Blue : Color.white;
                if (GUI.Button(layout.TabButtons[i], labels[i], m_TabStyle) && !selected)
                {
                    m_SelectedPage = pages[i];
                    GUI.FocusControl(string.Empty);
                }
                GUI.backgroundColor = oldBackground;
            }
        }

        string BuildAssetStatus(DentalNavigationSnapshot snapshot)
        {
            var thresholds = snapshot.HasThresholds
                ? $"阈值 v{snapshot.Thresholds.ConfigVersion}"
                : "阈值未同步";
            var ct = DentalCtVolumeService.Instance != null
                ? DentalCtVolumeService.Instance.StatusMessage
                : "CT 未加载";
            var transfer = m_RobotDisplay != null
                ? UserFacingStatus(m_RobotDisplay.TransferSummary)
                : "资产服务不可用";
            var context = snapshot.HasContext
                ? $"病例 {snapshot.CaseId} · 牙位 {snapshot.ToothId} · 规划 {snapshot.PlanId} · 钻针 {snapshot.ToolId}"
                : "病例上下文未同步";
            return $"{context}\n{thresholds} · {ct}\n{transfer}";
        }

        string BuildConnectionStatus(DentalNavigationSnapshot snapshot)
        {
            var endpoint = m_RobotDisplay != null ? m_RobotDisplay.ServerAddress : "—";
            var detail = m_RobotDisplay != null
                ? UserFacingStatus(m_RobotDisplay.ConnectionStatus)
                : "连接服务不可用";
            return $"{LinkPhrase(snapshot, DentalNavigationState.Instance != null ? DentalNavigationState.Instance.LastEvaluation : default)}\n当前地址：{endpoint}\n{detail}";
        }

        static string UserFacingStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "状态不可用";
            if (status.IndexOf("PlatformNotSupportedException", System.StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("gRPC requires extra configuration", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "当前运行环境无法建立 gRPC HTTP/2 连接；请在 Android 真机验证连接。";
            }
            return status.Trim();
        }

        static string DepthValue(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (evaluation.DashNumbers || !snapshot.HasDepthBreakdown)
                return "—";
            return snapshot.RemainingDepthMm < 0f
                ? $"超深 {Mathf.Abs(snapshot.RemainingDepthMm):0.0} mm"
                : $"{snapshot.RemainingDepthMm:0.0} mm";
        }

        static string DepthDetail(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (evaluation.DashNumbers || !snapshot.HasDepthBreakdown)
                return "当前 / 目标不可用";
            return $"当前 {snapshot.CurrentDepthMm:0.0} · 目标 {snapshot.TargetDepthMm:0.0} mm";
        }

        static string DirectionPhrase(float buccal, float mesial, string prefix, string suffix = "")
        {
            const float epsilon = 0.02f;
            var first = buccal > epsilon ? "颊侧" : buccal < -epsilon ? "舌侧" : string.Empty;
            var second = mesial > epsilon ? "近中" : mesial < -epsilon ? "远中" : string.Empty;
            var direction = string.IsNullOrEmpty(first)
                ? second
                : string.IsNullOrEmpty(second) ? first : first + "·" + second;
            return string.IsNullOrEmpty(direction) ? "方向居中" : prefix + direction + suffix;
        }

        static string LinkPhrase(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (snapshot.Link == DentalLinkState.Connecting)
                return "连接中";
            if (snapshot.Link == DentalLinkState.Live
                && !evaluation.DashNumbers
                && evaluation.Overall != DentalMetricGrade.Stale)
                return "已连接";
            if (snapshot.Link == DentalLinkState.Live)
                return "数据中断";
            return "未连接";
        }

        static Color LinkColor(DentalNavigationSnapshot snapshot, DentalHudEvaluation evaluation)
        {
            if (snapshot.Link == DentalLinkState.Live && !evaluation.DashNumbers
                && evaluation.Overall != DentalMetricGrade.Stale)
                return Green;
            if (snapshot.Link == DentalLinkState.Connecting || snapshot.Link == DentalLinkState.Live)
                return Amber;
            return Red;
        }

        static string OverallPhrase(DentalMetricGrade grade)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green:
                    return "在容差";
                case DentalMetricGrade.Amber:
                    return "接近";
                case DentalMetricGrade.Red:
                    return "超差";
                case DentalMetricGrade.Stale:
                    return "数据中断";
                default:
                    return "无数据";
            }
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
