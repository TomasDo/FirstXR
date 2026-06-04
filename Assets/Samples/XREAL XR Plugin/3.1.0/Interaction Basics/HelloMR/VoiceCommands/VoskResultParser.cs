using System.Text;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    public static class VoskResultParser
    {
        public static string ExtractText(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            const string key = "\"text\"";
            var index = json.IndexOf(key, System.StringComparison.Ordinal);
            if (index < 0)
                return string.Empty;

            index = json.IndexOf(':', index);
            if (index < 0)
                return string.Empty;

            index = json.IndexOf('"', index + 1);
            if (index < 0)
                return string.Empty;

            var end = json.IndexOf('"', index + 1);
            if (end < 0)
                return string.Empty;

            return json.Substring(index + 1, end - index - 1).Trim();
        }
    }
}
