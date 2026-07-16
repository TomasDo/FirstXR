using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Temporary Beam Pro overlay for navigation data received from the dental surgery robot.
    /// A gRPC client can feed this component from dental_model_transfer.proto messages.
    /// </summary>
    public class DentalRobotBeamProDisplay : MonoBehaviour
    {
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

        readonly double[] m_DrillFromTeeth = new double[16];
        readonly Dictionary<string, long> m_ReceivedBytesByModel = new Dictionary<string, long>();

        bool m_HasMetadata;
        bool m_HasTransferEnd;
        float m_LastUpdateRealtime;
        string m_Status = "等待手术机器人 gRPC 数据";
        string m_LastDatasetId = "-";
        string m_LastTransferMessage = "-";
        bool m_LastTransferOk;
        double m_Distance;
        double m_LateralDistance;
        double m_Angle;
        long m_TeethBytes;
        long m_DrillBytes;
        string m_EditableServerHost;
        string m_EditableServerPort;
        GUIStyle m_EndpointLabelStyle;
        GUIStyle m_EndpointFieldStyle;
        GUIStyle m_EndpointButtonStyle;

        public static DentalRobotBeamProDisplay Instance => s_Instance;

        public string ServerAddress => $"{m_ServerHost}:{m_ServerPort}";

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
            AppendLog($"面板已启动，等待连接 {ServerAddress}");
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void SetConnectionStatus(string status)
        {
            m_Status = string.IsNullOrEmpty(status) ? "等待手术机器人 gRPC 数据" : status;
            AppendLog(m_Status);
        }

        public void ApplyMetadata(string datasetId, IList<double> drillFromTeeth, double distance, double lateralDistance, double angle)
        {
            m_HasMetadata = true;
            m_LastUpdateRealtime = Time.realtimeSinceStartup;
            m_LastDatasetId = string.IsNullOrEmpty(datasetId) ? "-" : datasetId;
            m_Distance = distance;
            m_LateralDistance = lateralDistance;
            m_Angle = angle;
            m_Status = "已收到导航 metadata";

            for (var i = 0; i < m_DrillFromTeeth.Length; i++)
                m_DrillFromTeeth[i] = drillFromTeeth != null && i < drillFromTeeth.Count ? drillFromTeeth[i] : 0d;

            AppendLog($"metadata dataset={m_LastDatasetId}, distance={m_Distance:0.###}, lateral={m_LateralDistance:0.###}, angle={m_Angle:0.###}");
            if (DentalRobotModelRenderer.Instance != null)
                DentalRobotModelRenderer.Instance.ApplyMetadata(drillFromTeeth);
        }

        public void ApplyStlChunk(string datasetId, string modelType, string filename, long offset, int byteCount, byte[] data)
        {
            m_LastUpdateRealtime = Time.realtimeSinceStartup;
            m_LastDatasetId = string.IsNullOrEmpty(datasetId) ? m_LastDatasetId : datasetId;

            var key = string.IsNullOrEmpty(modelType) ? "UNKNOWN" : modelType;
            var total = offset + Mathf.Max(0, byteCount);
            if (!m_ReceivedBytesByModel.TryGetValue(key, out var previous) || total > previous)
                m_ReceivedBytesByModel[key] = total;

            m_Status = "正在接收 STL 模型分块";
            AppendLog($"stl {key} {filename} offset={offset}, bytes={byteCount}");

            if (DentalRobotModelRenderer.Instance != null)
                DentalRobotModelRenderer.Instance.ApplyStlChunk(ParseModelType(key), filename, offset, data);
        }

        public void ApplyTransferEnd(string datasetId, bool ok, string message, long teethBytes, long drillBytes)
        {
            m_HasTransferEnd = true;
            m_LastUpdateRealtime = Time.realtimeSinceStartup;
            m_LastDatasetId = string.IsNullOrEmpty(datasetId) ? m_LastDatasetId : datasetId;
            m_LastTransferOk = ok;
            m_LastTransferMessage = string.IsNullOrEmpty(message) ? "-" : message;
            m_TeethBytes = teethBytes;
            m_DrillBytes = drillBytes;
            m_Status = ok ? "模型传输完成" : "模型传输失败";
            AppendLog($"end ok={ok}, teeth={teethBytes} B, drill={drillBytes} B, message={m_LastTransferMessage}");

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
            BeamProUnifiedLogWindow.SetStatus("手术机器人", BuildStatusText());
            BeamProUnifiedLogWindow.AddLine("手术机器人", message);
        }

        void OnGUI()
        {
            if (!m_ShowOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            BeamProUnifiedLogWindow.SetStatus("手术机器人", BuildStatusText());
            DrawEndpointControls();
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
            GUI.Label(new Rect(portLabelX, rect.y, portLabelWidth, rect.height), "端口", m_EndpointLabelStyle);
            m_EditableServerPort = GUI.TextField(
                new Rect(portLabelX + portLabelWidth + gap, rect.y, portFieldWidth, rect.height),
                m_EditableServerPort ?? string.Empty,
                5,
                m_EndpointFieldStyle);

            var buttonX = portLabelX + portLabelWidth + gap + portFieldWidth + gap;
            if (GUI.Button(new Rect(buttonX, rect.y, buttonWidth, rect.height), "开始搜索", m_EndpointButtonStyle))
                StartEndpointSearch();
        }

        void StartEndpointSearch()
        {
            var host = (m_EditableServerHost ?? string.Empty).Trim();
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                SetEndpointValidationError("请输入有效的 IPv4 地址。");
                return;
            }

            if (!int.TryParse(m_EditableServerPort, out var port) || port < 1 || port > 65535)
            {
                SetEndpointValidationError("端口号必须在 1-65535 之间。");
                return;
            }

            ConfigureEndpoint(host, port, m_DeviceId, m_DatasetId);

            var client = FindObjectOfType<DentalRobotGrpcClient>();
            if (client == null)
            {
                SetEndpointValidationError("未找到 DentalRobotGrpcClient。");
                return;
            }

            AppendLog($"手动搜索 gRPC 服务端 {ServerAddress}");
            client.SearchEndpoint(m_ServerHost, m_ServerPort);
        }

        void SetEndpointValidationError(string message)
        {
            SetConnectionStatus(message);
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

        string BuildStatusText()
        {
            var builder = new StringBuilder(512);
            builder.Append("状态: ").AppendLine(m_Status);
            builder.Append("gRPC: ").Append(ServerAddress)
                .Append(" | device_id: ").Append(m_DeviceId)
                .Append(" | request dataset: ").AppendLine(m_DatasetId);
            builder.Append("dataset: ").Append(m_LastDatasetId)
                .Append(" | last update: ").AppendLine(GetLastUpdateText());

            if (m_HasMetadata)
            {
                builder.Append("distance: ").Append(m_Distance.ToString("0.###"))
                    .Append(" | lateral: ").Append(m_LateralDistance.ToString("0.###"))
                    .Append(" | angle: ").AppendLine(m_Angle.ToString("0.###"));
                builder.AppendLine(BuildMatrixText());
            }
            else
            {
                builder.AppendLine("尚未收到 ModelMetadata。机器人端开始 StreamDentalModel 后这里会更新。");
            }

            builder.AppendLine(BuildTransferText());
            return builder.ToString();
        }

        string GetLastUpdateText()
        {
            if (m_LastUpdateRealtime <= 0f)
                return "-";

            return $"{Time.realtimeSinceStartup - m_LastUpdateRealtime:0.0}s ago";
        }

        string BuildMatrixText()
        {
            var builder = new StringBuilder(160);
            builder.AppendLine("drill_from_teeth:");
            for (var row = 0; row < 4; row++)
            {
                builder.Append("  ");
                for (var col = 0; col < 4; col++)
                    builder.Append(m_DrillFromTeeth[row * 4 + col].ToString("0.###")).Append(col == 3 ? string.Empty : ", ");
                if (row < 3)
                    builder.AppendLine();
            }

            return builder.ToString();
        }

        string BuildTransferText()
        {
            if (m_HasTransferEnd)
                return $"transfer: {(m_LastTransferOk ? "ok" : "failed")} | teeth: {m_TeethBytes} B | drill: {m_DrillBytes} B | {m_LastTransferMessage}";

            if (m_ReceivedBytesByModel.Count == 0)
                return "transfer: 尚未收到 STL 分块";

            var builder = new StringBuilder("transfer:");
            foreach (var item in m_ReceivedBytesByModel)
                builder.Append(' ').Append(item.Key).Append('=').Append(item.Value).Append(" B");
            return builder.ToString();
        }
    }
}
