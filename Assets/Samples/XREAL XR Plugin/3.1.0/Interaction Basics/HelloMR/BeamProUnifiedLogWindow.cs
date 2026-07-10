using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public class BeamProUnifiedLogWindow : MonoBehaviour
    {
        class SourceLog
        {
            public string Status = string.Empty;
            public readonly List<string> Lines = new List<string>(32);
        }

        const int MaxLinesPerSource = 32;
        const float HeaderHeight = 30f;
        const float Padding = 10f;

        static BeamProUnifiedLogWindow s_Instance;
        static readonly Dictionary<string, SourceLog> s_SourceLogs = new Dictionary<string, SourceLog>();
        static readonly List<string> s_SourceOrder = new List<string>();

        Vector2 m_Scroll;
        GUIStyle m_PanelStyle;
        GUIStyle m_HeaderStyle;
        GUIStyle m_TextStyle;

        public static BeamProUnifiedLogWindow Instance
        {
            get
            {
                EnsureInstance();
                return s_Instance;
            }
        }

        public static void EnsureInstance()
        {
            if (s_Instance != null)
                return;

            var existing = FindObjectOfType<BeamProUnifiedLogWindow>();
            if (existing != null)
            {
                s_Instance = existing;
                return;
            }

            var obj = new GameObject("Beam Pro Unified Log Window");
            s_Instance = obj.AddComponent<BeamProUnifiedLogWindow>();
        }

        public static void SetStatus(string source, string status)
        {
            var log = GetSourceLog(source);
            log.Status = status ?? string.Empty;
        }

        public static void AddLine(string source, string message)
        {
            var log = GetSourceLog(source);
            log.Lines.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");
            while (log.Lines.Count > MaxLinesPerSource)
                log.Lines.RemoveAt(0);
        }

        static SourceLog GetSourceLog(string source)
        {
            EnsureInstance();
            source = string.IsNullOrEmpty(source) ? "Log" : source;

            if (!s_SourceLogs.TryGetValue(source, out var log))
            {
                log = new SourceLog();
                s_SourceLogs[source] = log;
                s_SourceOrder.Add(source);
            }

            return log;
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void OnGUI()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            EnsureStyles();
            var rect = BeamProOverlayLayout.GetMainLogRect(BeamProOverlayLayout.MaxButtonRows);

            GUI.depth = 9;
            GUI.Box(rect, GUIContent.none, m_PanelStyle);

            var headerRect = new Rect(rect.x + Padding, rect.y + 6f, rect.width - Padding * 2f, HeaderHeight);
            GUI.Label(headerRect, "Beam Pro Logs", m_HeaderStyle);

            var text = BuildLogText();
            var contentRect = new Rect(
                rect.x + Padding,
                rect.y + HeaderHeight + Padding,
                rect.width - Padding * 2f,
                rect.height - HeaderHeight - Padding * 2f);
            var innerWidth = contentRect.width - 24f;
            var contentHeight = Mathf.Max(contentRect.height, m_TextStyle.CalcHeight(new GUIContent(text), innerWidth));

            m_Scroll = GUI.BeginScrollView(contentRect, m_Scroll, new Rect(0f, 0f, innerWidth, contentHeight));
            GUI.Label(new Rect(0f, 0f, innerWidth, contentHeight), text, m_TextStyle);
            GUI.EndScrollView();
        }

        string BuildLogText()
        {
            if (s_SourceOrder.Count == 0)
                return "暂无日志";

            var builder = new StringBuilder(2048);
            foreach (var source in s_SourceOrder)
            {
                if (!s_SourceLogs.TryGetValue(source, out var log))
                    continue;

                builder.Append('[').Append(source).AppendLine("]");
                if (!string.IsNullOrEmpty(log.Status))
                    builder.AppendLine(log.Status);

                for (var i = log.Lines.Count - 1; i >= 0; i--)
                    builder.AppendLine(log.Lines[i]);

                builder.AppendLine();
            }

            return builder.ToString();
        }

        void EnsureStyles()
        {
            var fontSize = Mathf.Max(13, Screen.height / 72);
            if (m_TextStyle != null && m_TextStyle.fontSize == fontSize)
                return;

            m_PanelStyle = new GUIStyle(GUI.skin.box);
            m_HeaderStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = fontSize + 2,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.72f, 0.95f, 1f) }
            };
            m_TextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                wordWrap = true,
                richText = false,
                normal = { textColor = Color.white }
            };
        }
    }
}
