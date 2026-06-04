using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    public sealed class VoiceCommandBeamProPanel
    {
        public struct LogEntry
        {
            public string Time;
            public string RawText;
            public string MatchedDisplay;
            public string Status;
            public float Confidence;
        }

        readonly List<LogEntry> m_Entries = new List<LogEntry>(32);
        readonly int m_MaxEntries;
        Vector2 m_Scroll;
        Vector2 m_Position;
        GUIStyle m_BoxStyle;
        GUIStyle m_TextStyle;
        string m_StatusLine = "语音口令：初始化中";

        public IReadOnlyList<LogEntry> Entries => m_Entries;

        public VoiceCommandBeamProPanel(int maxEntries = 24)
        {
            m_MaxEntries = maxEntries;
            m_Position = new Vector2(16f, Screen.height * 0.42f);
        }

        public void SetStatus(string status) => m_StatusLine = status;

        public void AddEntry(string rawText, string matchedDisplay, string status, float confidence)
        {
            m_Entries.Add(new LogEntry
            {
                Time = System.DateTime.Now.ToString("HH:mm:ss"),
                RawText = rawText ?? string.Empty,
                MatchedDisplay = matchedDisplay ?? "-",
                Status = status ?? string.Empty,
                Confidence = confidence,
            });

            while (m_Entries.Count > m_MaxEntries)
                m_Entries.RemoveAt(0);
        }

        public void Clear()
        {
            m_Entries.Clear();
            m_Scroll = Vector2.zero;
        }

        public void Draw()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            const float width = 520f;
            const float height = 300f;
            var rect = new Rect(m_Position.x, m_Position.y, width, height);

            EnsureStyles();
            GUI.depth = 12;
            GUI.Box(rect, GUIContent.none, m_BoxStyle);

            var headerRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 44f);
            GUI.Label(headerRect, "语音口令识别", m_TextStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, 20f), m_StatusLine, m_TextStyle);

            var content = BuildLogText();
            var inner = new Rect(rect.x + 8f, rect.y + 54f, rect.width - 16f, rect.height - 62f);
            m_Scroll = GUI.BeginScrollView(inner, m_Scroll, new Rect(0f, 0f, inner.width - 24f, Mathf.Max(inner.height, 24f * m_Entries.Count + 40f)));
            GUI.Label(new Rect(0f, 0f, inner.width - 28f, 2000f), content, m_TextStyle);
            GUI.EndScrollView();
        }

        string BuildLogText()
        {
            if (m_Entries.Count == 0)
                return "（暂无识别记录）";

            var builder = new StringBuilder(512);
            for (var i = m_Entries.Count - 1; i >= 0; i--)
            {
                var e = m_Entries[i];
                builder.Append('[').Append(e.Time).Append("] ").Append(e.Status);
                if (!string.IsNullOrEmpty(e.RawText))
                    builder.Append(" | 识别: ").Append(e.RawText);
                if (!string.IsNullOrEmpty(e.MatchedDisplay) && e.MatchedDisplay != "-")
                    builder.Append(" | 口令: ").Append(e.MatchedDisplay);
                if (e.Confidence > 0f)
                    builder.Append(" (").Append(e.Confidence.ToString("0.00")).Append(')');
                builder.AppendLine();
            }

            return builder.ToString();
        }

        void EnsureStyles()
        {
            var fontSize = Mathf.Max(13, Screen.height / 72);
            if (m_TextStyle != null && m_TextStyle.fontSize == fontSize)
                return;

            m_TextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                wordWrap = true,
                richText = false,
            };
            m_TextStyle.normal.textColor = new Color(1f, 0.92f, 0.55f);

            m_BoxStyle = new GUIStyle(GUI.skin.box);
        }
    }
}
