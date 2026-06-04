using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    [Serializable]
    public class VoiceCommandEntry
    {
        public string id;
        public string displayName;
        public string[] phrases;
        public string action;
    }

    [Serializable]
    public class VoiceCommandTableData
    {
        public int version;
        public string language;
        public string modelFolderName = "vosk-model-small-cn-0.22";
        public float minConfidence = 0.55f;
        public float cooldownSeconds = 1.2f;
        public VoiceCommandEntry[] commands;
    }

    public sealed class VoiceCommandTable
    {
        public VoiceCommandTableData Data { get; private set; }
        readonly Dictionary<string, VoiceCommandEntry> m_ById = new Dictionary<string, VoiceCommandEntry>();
        readonly List<string> m_AllPhrases = new List<string>();

        public float MinConfidence => Data?.minConfidence ?? 0.55f;
        public float CooldownSeconds => Data?.cooldownSeconds ?? 1.2f;
        public string ModelFolderName => Data?.modelFolderName ?? "vosk-model-small-cn-0.22";
        public IReadOnlyList<string> AllPhrases => m_AllPhrases;

        public static VoiceCommandTable LoadFromStreamingAssets(string relativePath = "VoiceCommands/commands_zh.json")
        {
            var jsonPath = Path.Combine(Application.streamingAssetsPath, relativePath);
            var json = ReadStreamingAssetsText(jsonPath);
            if (string.IsNullOrEmpty(json))
                throw new FileNotFoundException($"Voice command table not found: {jsonPath}");

            var data = JsonUtility.FromJson<VoiceCommandTableData>(json);
            if (data?.commands == null || data.commands.Length == 0)
                throw new InvalidDataException("Voice command table has no commands.");

            return new VoiceCommandTable(data);
        }

        VoiceCommandTable(VoiceCommandTableData data)
        {
            Data = data;
            m_AllPhrases.Clear();
            m_ById.Clear();

            foreach (var entry in data.commands)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id))
                    continue;

                m_ById[entry.id] = entry;
                if (entry.phrases == null)
                    continue;

                foreach (var phrase in entry.phrases)
                {
                    if (!string.IsNullOrWhiteSpace(phrase))
                        m_AllPhrases.Add(phrase.Trim());
                }
            }
        }

        public string BuildVoskGrammarJson()
        {
            var builder = new StringBuilder(256 + m_AllPhrases.Count * 16);
            builder.Append('[');
            for (var i = 0; i < m_AllPhrases.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append('"');
                builder.Append(EscapeJsonString(m_AllPhrases[i]));
                builder.Append('"');
            }

            builder.Append(",\"[unk]\"]");
            return builder.ToString();
        }

        public bool TryMatch(string recognizedText, out VoiceCommandEntry entry, out string matchedPhrase, out float score)
        {
            entry = null;
            matchedPhrase = null;
            score = 0f;

            var normalized = NormalizeText(recognizedText);
            if (string.IsNullOrEmpty(normalized))
                return false;

            var bestScore = 0f;
            VoiceCommandEntry bestEntry = null;
            string bestPhrase = null;

            foreach (var command in Data.commands)
            {
                if (command?.phrases == null)
                    continue;

                foreach (var phrase in command.phrases)
                {
                    var phraseNorm = NormalizeText(phrase);
                    if (string.IsNullOrEmpty(phraseNorm))
                        continue;

                    var candidateScore = ScorePhrase(normalized, phraseNorm);
                    if (candidateScore > bestScore)
                    {
                        bestScore = candidateScore;
                        bestEntry = command;
                        bestPhrase = phrase;
                    }
                }
            }

            if (bestEntry == null || bestScore < MinConfidence)
                return false;

            entry = bestEntry;
            matchedPhrase = bestPhrase;
            score = bestScore;
            return true;
        }

        public bool TryGetById(string id, out VoiceCommandEntry entry) => m_ById.TryGetValue(id, out entry);

        static float ScorePhrase(string recognized, string phrase)
        {
            if (recognized == phrase)
                return 1f;

            if (recognized.Contains(phrase))
                return 0.92f;

            if (phrase.Contains(recognized) && recognized.Length >= 2)
                return 0.8f;

            var distance = LevenshteinDistance(recognized, phrase);
            var maxLen = Mathf.Max(recognized.Length, phrase.Length);
            if (maxLen == 0)
                return 0f;

            var similarity = 1f - (float)distance / maxLen;
            return similarity >= 0.72f ? similarity * 0.9f : 0f;
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text.Trim().ToLowerInvariant())
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                    continue;
                builder.Append(ch);
            }

            return builder.ToString();
        }

        static string EscapeJsonString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static int LevenshteinDistance(string a, string b)
        {
            var n = a.Length;
            var m = b.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++)
                d[i, 0] = i;
            for (var j = 0; j <= m; j++)
                d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        static string ReadStreamingAssetsText(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!path.Contains("://"))
                path = "file://" + path;

            using var request = UnityEngine.Networking.UnityWebRequest.Get(path);
            var operation = request.SendWebRequest();
            while (!operation.isDone) { }

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                return null;

            return request.downloadHandler.text;
#else
            return File.Exists(path) ? File.ReadAllText(path) : null;
#endif
        }
    }
}
