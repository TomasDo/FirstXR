using System.Net;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Beam Pro assistant overlay: endpoint controls plus a live navigation dashboard.
    /// </summary>
    public class DentalRobotBeamProDisplay : MonoBehaviour
    {
        const float StatusSummaryIntervalSeconds = 1f;

        static readonly Color Green = new Color(0.239f, 0.863f, 0.592f, 1f);
        static readonly Color Amber = new Color(0.961f, 0.773f, 0.094f, 1f);
        static readonly Color Red = new Color(1f, 0.302f, 0.302f, 1f);
        static readonly Color Gray = new Color(0.604f, 0.639f, 0.698f, 1f);
        static readonly Color TextPrimary = Color.white;

        [SerializeField]
        bool m_ShowOnBeamPro = true;

        [SerializeField]
        string m_ServerHost = DentalRobotConnectionDefaults.ServerHost;

        [SerializeField]
        int m_ServerPort = DentalRobotConnectionDefaults.ServerPort;

        [SerializeField]
        string m_DeviceId = DentalRobotConnectionDefaults.DeviceId;

        [SerializeField]
        string m_DatasetId = DentalRobotConnectionDefaults.DatasetId;

        static DentalRobotBeamProDisplay s_Instance;

        string m_CachedServerAddress;
        string m_CachedAddressHost;
        int m_CachedAddressPort;
        string m_CachedTransferSummary;
        string m_Status = "Waiting for surgical robot gRPC data";
        bool m_LastTransferOk;
        bool m_HasTransferEnd;
        long m_TeethBytes;
        long m_DrillBytes;
        string m_EditableServerHost;
        string m_EditableServerPort;
        float m_NextStatusSummaryRealtime;
        GUIStyle m_EndpointLabelStyle;
        GUIStyle m_EndpointFieldStyle;
        GUIStyle m_EndpointButtonStyle;
        GUIStyle m_TitleStyle;
        GUIStyle m_ValueStyle;
        GUIStyle m_CaptionStyle;
        GUIStyle m_AlarmStyle;

        public static DentalRobotBeamProDisplay Instance => s_Instance;

        public string ServerAddress
        {
            get
            {
                if (m_CachedServerAddress == null || m_CachedAddressHost != m_ServerHost
                    || m_CachedAddressPort != m_ServerPort)
                {
                    m_CachedAddressHost = m_ServerHost;
                    m_CachedAddressPort = m_ServerPort;
                    m_CachedServerAddress = $"{m_ServerHost}:{m_ServerPort}";
                }
                return m_CachedServerAddress;
            }
        }
        public string ServerHost => m_ServerHost;
        public int ServerPort => m_ServerPort;
        public string ConnectionStatus => m_Status;
        public string TransferSummary => m_HasTransferEnd
            ? m_CachedTransferSummary
            : m_Status;

        public void ConfigureEndpoint(string serverHost, int serverPort, string deviceId, string datasetId)
        {
            if (!string.IsNullOrEmpty(serverHost))
                m_ServerHost = serverHost;

            if (serverPort > 0)
                m_ServerPort = serverPort;

            if (!string.IsNullOrEmpty(deviceId))
                m_DeviceId = deviceId;

            if (!string.IsNullOrEmpty(datasetId))
                m_DatasetId = datasetId;

            m_EditableServerHost = m_ServerHost;
            m_EditableServerPort = m_ServerPort.ToString();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            m_EditableServerHost = m_ServerHost;
            m_EditableServerPort = m_ServerPort.ToString();
            AppendLog($"Dashboard ready; waiting to connect to {ServerAddress}");
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void Update()
        {
            // Status publication must not depend on which Beam Pro page is visible.
            PublishStatusSummary();
        }

        public void SetConnectionStatus(string status)
        {
            m_Status = string.IsNullOrEmpty(status) ? "Waiting for surgical robot gRPC data" : status;
            AppendLog(m_Status);
        }

        public bool ApplyStlChunk(string datasetId, string modelType, string filename, long offset, int byteCount, byte[] data)
        {
            var key = string.IsNullOrEmpty(modelType) ? "UNKNOWN" : modelType;
            m_Status = "Receiving STL model chunks";
            if (offset == 0)
                AppendLog($"stl {key} {filename} start");

            if (DentalRobotModelRenderer.Instance != null)
                return DentalRobotModelRenderer.Instance.TryApplyStlChunk(ParseModelType(key), filename, offset, data);
            return false;
        }

        public void ApplyTransferEnd(string datasetId, bool ok, string message, long teethBytes, long drillBytes)
        {
            m_HasTransferEnd = true;
            m_LastTransferOk = ok;
            m_TeethBytes = teethBytes;
            m_DrillBytes = drillBytes;
            m_CachedTransferSummary = $"Model {(ok ? "complete" : "failed")} · teeth {teethBytes} B · drill {drillBytes} B";
            m_Status = ok ? "Model transfer complete" : "Model transfer failed";
            var transferMessage = string.IsNullOrEmpty(message) ? "-" : message;
            AppendLog($"end ok={ok}, teeth={teethBytes} B, drill={drillBytes} B, message={transferMessage}");

            if (ok && DentalRobotModelRenderer.Instance != null)
                DentalRobotModelRenderer.Instance.ApplyTransferEnd(teethBytes, drillBytes);
        }

        static DentalRobotModelRenderer.DentalModelType ParseModelType(string modelType)
        {
            if (string.Equals(modelType, "TEETH", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelType, "1", System.StringComparison.OrdinalIgnoreCase))
                return DentalRobotModelRenderer.DentalModelType.Teeth;

            if (string.Equals(modelType, "DRILL", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelType, "2", System.StringComparison.OrdinalIgnoreCase))
                return DentalRobotModelRenderer.DentalModelType.Drill;

            return DentalRobotModelRenderer.DentalModelType.Unknown;
        }

        public void AppendLog(string message)
        {
            BeamProUnifiedLogWindow.AddLine("Surgical robot", message);
        }

        void OnGUI()
        {
            if (!m_ShowOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            if (BeamProPagedController.IsActive)
                return;

            DrawEndpointControls();
            if (!BeamProUnifiedLogWindow.IsVisible)
                DrawDashboard();
        }

        void PublishStatusSummary()
        {
            if (Time.realtimeSinceStartup < m_NextStatusSummaryRealtime)
                return;

            m_NextStatusSummaryRealtime = Time.realtimeSinceStartup + StatusSummaryIntervalSeconds;
            var state = DentalNavigationState.Instance;
            if (state == null)
                return;

            var snap = state.Capture(Time.realtimeSinceStartup);
            var eval = state.LastEvaluation;
            var depth = eval.DashNumbers || !snap.HasDepthBreakdown
                ? "—"
                : snap.RemainingDepthMm.ToString("0.0") + "mm";
            var lateral = eval.DashNumbers ? "—" : snap.LateralMm.ToString("0.0") + "mm";
            var angle = eval.DashNumbers ? "—" : snap.AngleDeg.ToString("0.0") + "°";
            BeamProUnifiedLogWindow.SetStatus("Surgical robot",
                $"{m_Status} | {depth} | {lateral} | {angle} | {DentalNavigationPresentation.BeamProOverallPhrase(eval.Overall)}");
        }

        void DrawDashboard()
        {
            EnsureDashboardStyles();
            var rect = BeamProOverlayLayout.GetMainLogRect(BeamProOverlayLayout.MaxButtonRows);
            GUI.depth = 9;

            var previous = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.88f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = previous;

            var state = DentalNavigationState.Instance;
            var snap = state != null
                ? state.Capture(Time.realtimeSinceStartup)
                : default;
            var eval = state != null ? state.LastEvaluation : default;

            var pad = 16f;
            var y = rect.y + 8f + BeamProOverlayLayout.DentalEndpointControlsHeight + 12f;
            var x = rect.x + pad;
            var width = rect.width - pad * 2f;

            GUI.Label(new Rect(x, y, width, 36f), "Surgical navigation · XREAL", m_TitleStyle);
            y += 40f;

            var linkText = DentalConnectionPresentation.Label(snap.Link);
            var previousContent = GUI.contentColor;
            GUI.contentColor = DentalConnectionPresentation.IndicatorColor(snap.Link, Green, Amber, Red);
            GUI.Label(new Rect(x, y, width, 32f),
                $"{linkText}    {ContextPhrase(snap)}    {AgeText(snap, eval)}",
                m_CaptionStyle);
            GUI.contentColor = previousContent;
            y += 40f;

            var cellWidth = (width - 24f) / 2f;
            var cellHeight = Mathf.Max(90f, (rect.yMax - y - 80f) * 0.42f);
            DrawMetricCell(new Rect(x, y, cellWidth, cellHeight), "Remaining depth",
                FormatMetric(snap.RemainingDepthMm, "mm", eval.DashNumbers || !snap.HasDepthBreakdown), eval.Depth, false);
            DrawMetricCell(new Rect(x + cellWidth + 24f, y, cellWidth, cellHeight), "Position offset",
                eval.DashNumbers
                    ? "—"
                    : $"{snap.LateralMm:0.0} mm\n{(snap.HasLateralDirection ? DentalNavigationPresentation.DirectionPhrase(snap.LateralBuccalMm, snap.LateralMesialMm, "toward ") : "Direction unavailable")}",
                eval.Lateral, true);
            y += cellHeight + 16f;
            DrawMetricCell(new Rect(x, y, cellWidth, cellHeight), "Angle deviation",
                eval.DashNumbers
                    ? "—"
                    : $"{snap.AngleDeg:0.0}°\n{(snap.HasTiltDirection ? DentalNavigationPresentation.DirectionPhrase(snap.TiltBuccalDeg, snap.TiltMesialDeg, "tilt toward ") : "Direction unavailable")}",
                eval.Angle, true);
            DrawMetricCell(new Rect(x + cellWidth + 24f, y, cellWidth, cellHeight), "Overall", DentalNavigationPresentation.BeamProOverallPhrase(eval.Overall), eval.Overall, true);
            y += cellHeight + 16f;

            if (eval.ShowAlarm && !string.IsNullOrEmpty(eval.AlarmText))
            {
                GUI.contentColor = Red;
                GUI.Label(new Rect(x, y, width, 40f), eval.AlarmText, m_AlarmStyle);
                GUI.contentColor = previousContent;
                y += 44f;
            }

            GUI.contentColor = Gray;
            var ct = DentalCtVolumeService.Instance;
            var ctStatus = ct != null ? ct.StatusMessage : "CT not loaded";
            var thresholdStatus = snap.HasThresholds ? $"Thresholds v{snap.Thresholds.ConfigVersion}" : "Thresholds not synced";
            var transfer = m_HasTransferEnd
                ? $"Model {(m_LastTransferOk ? "complete" : "failed")}  teeth {m_TeethBytes} B  drill {m_DrillBytes} B"
                : m_Status;
            GUI.Label(new Rect(x, Mathf.Min(y, rect.yMax - 54f), width, 52f),
                $"{thresholdStatus} · {ctStatus}\n{transfer}", m_CaptionStyle);
            GUI.contentColor = previousContent;
        }

        void DrawMetricCell(Rect rect, string title, string value, DentalMetricGrade grade, bool allowGreen)
        {
            var previous = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.14f, 0.95f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = previous;

            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 28f), title, m_CaptionStyle);
            var previousContent = GUI.contentColor;
            GUI.contentColor = ColorForGrade(grade, allowGreen);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 36f, rect.width - 24f, rect.height - 48f), value, m_ValueStyle);
            GUI.contentColor = previousContent;
        }

        void DrawEndpointControls()
        {
            EnsureEndpointStyles();
            var rect = BeamProOverlayLayout.GetDentalEndpointControlsRect();
            var gap = 6f;
            var labelWidth = Mathf.Clamp(rect.width * 0.06f, 26f, 42f);
            var portLabelWidth = Mathf.Clamp(rect.width * 0.09f, 38f, 58f);
            var portFieldWidth = Mathf.Clamp(rect.width * 0.14f, 54f, 90f);
            var buttonWidth = Mathf.Clamp(rect.width * 0.22f, 88f, 150f);
            var hostFieldWidth = rect.width - labelWidth - portLabelWidth - portFieldWidth - buttonWidth - gap * 4f;

            GUI.depth = 0;
            GUI.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), "IP", m_EndpointLabelStyle);
            m_EditableServerHost = GUI.TextField(
                new Rect(rect.x + labelWidth + gap, rect.y, Mathf.Max(60f, hostFieldWidth), rect.height),
                m_EditableServerHost ?? string.Empty,
                64,
                m_EndpointFieldStyle);

            var portLabelX = rect.x + labelWidth + gap + Mathf.Max(60f, hostFieldWidth) + gap;
            GUI.Label(new Rect(portLabelX, rect.y, portLabelWidth, rect.height), "Port", m_EndpointLabelStyle);
            m_EditableServerPort = GUI.TextField(
                new Rect(portLabelX + portLabelWidth + gap, rect.y, portFieldWidth, rect.height),
                m_EditableServerPort ?? string.Empty,
                5,
                m_EndpointFieldStyle);

            var buttonX = portLabelX + portLabelWidth + gap + portFieldWidth + gap;
            if (GUI.Button(new Rect(buttonX, rect.y, buttonWidth, rect.height), "Search", m_EndpointButtonStyle))
                StartEndpointSearch();
        }

        public bool TryStartEndpointSearch(string hostText, string portText, out string result)
        {
            var host = (hostText ?? string.Empty).Trim();
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                result = "Enter a valid IPv4 address.";
                SetConnectionStatus(result);
                return false;
            }

            if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
            {
                result = "Port must be between 1 and 65535.";
                SetConnectionStatus(result);
                return false;
            }

            ConfigureEndpoint(host, port, m_DeviceId, m_DatasetId);

            var client = FindFirstObjectByType<DentalRobotGrpcClient>();
            if (client == null)
            {
                result = "DentalRobotGrpcClient not found.";
                SetConnectionStatus(result);
                return false;
            }

            AppendLog($"Manually searching for gRPC server {ServerAddress}");
            result = $"Connecting to {ServerAddress}";
            SetConnectionStatus(result);
            client.SearchEndpoint(m_ServerHost, m_ServerPort);
            return true;
        }

        void StartEndpointSearch()
        {
            TryStartEndpointSearch(m_EditableServerHost, m_EditableServerPort, out _);
        }

        void EnsureEndpointStyles()
        {
            var fontSize = Mathf.Max(14, Screen.height / 64);
            if (m_EndpointFieldStyle != null && m_EndpointFieldStyle.fontSize == fontSize)
                return;

            m_EndpointLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = fontSize,
                normal = { textColor = Color.white }
            };
            m_EndpointFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = fontSize
            };
            m_EndpointButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };
        }

        void EnsureDashboardStyles()
        {
            var captionSize = Mathf.Max(18, Screen.height / 48);
            if (m_CaptionStyle != null && m_CaptionStyle.fontSize == captionSize)
                return;

            m_TitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = captionSize + 8,
                fontStyle = FontStyle.Bold,
                normal = { textColor = TextPrimary }
            };
            m_CaptionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = captionSize,
                normal = { textColor = Gray }
            };
            m_ValueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(36, Screen.height / 18),
                fontStyle = FontStyle.Bold,
                normal = { textColor = TextPrimary }
            };
            m_AlarmStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = captionSize + 4,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Red }
            };
        }

        static string FormatMetric(float value, string unit, bool dash)
        {
            return dash ? "—" : $"{value:0.0} {unit}";
        }

        static string AgeText(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            if (!eval.ShowAge || float.IsInfinity(snap.AgeSeconds))
                return string.Empty;
            return $"{Mathf.RoundToInt(snap.AgeSeconds * 1000f)}ms";
        }

        static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text == "-" || text == "—")
                return "—";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        static string ContextPhrase(DentalNavigationSnapshot snap)
        {
            if (!snap.HasContext)
                return Truncate(snap.DatasetId, 16);
            return $"{Truncate(snap.ToothId, 8)} · {Truncate(snap.ToolId, 8)}";
        }

        static Color ColorForGrade(DentalMetricGrade grade, bool allowGreen)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green:
                    return allowGreen ? Green : TextPrimary;
                case DentalMetricGrade.Amber:
                    return Amber;
                case DentalMetricGrade.Red:
                    return Red;
                default:
                    return Gray;
            }
        }
    }
}
