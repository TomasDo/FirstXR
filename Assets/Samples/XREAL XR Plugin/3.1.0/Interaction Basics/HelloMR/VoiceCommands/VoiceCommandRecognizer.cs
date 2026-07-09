using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using Vosk;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    public sealed class VoiceCommandRecognizer : MonoBehaviour
    {
        const int MaxPendingUtterances = 3;

        [SerializeField]
        bool m_DisableVoiceRecognition = true;

        [SerializeField]
        string m_CommandTableRelativePath = "VoiceCommands/commands_zh.json";

        [SerializeField]
        XREALMicrophoneStream m_MicrophoneStream;

        [SerializeField]
        HelloMRVoiceCommandBridge m_CommandBridge;

        [SerializeField]
        bool m_StartWhenMicReady = true;

        [SerializeField]
        bool m_ShowBeamProPanel = true;

        VoiceCommandTable m_Table;
        VoiceCommandBeamProPanel m_Panel;
        PcmUtteranceDetector m_Detector;
        VoskModel m_Model;
        VoskGrammarRecognizer m_Recognizer;
        string m_GrammarJson;

        bool m_ListeningEnabled = true;
        bool m_IsReady;
        bool m_IsInitializing;
        float m_NextAllowedDispatchTime;
        readonly Queue<byte[]> m_PendingUtterances = new Queue<byte[]>(4);
        readonly object m_UtteranceLock = new object();

        void Awake()
        {
            if (m_DisableVoiceRecognition)
                return;

            if (m_MicrophoneStream == null)
                m_MicrophoneStream = FindObjectOfType<XREALMicrophoneStream>();
            if (m_CommandBridge == null)
                m_CommandBridge = FindObjectOfType<HelloMRVoiceCommandBridge>();
            if (m_CommandBridge == null)
                m_CommandBridge = gameObject.AddComponent<HelloMRVoiceCommandBridge>();

            m_Panel = new VoiceCommandBeamProPanel();
        }

        void OnEnable()
        {
            if (m_DisableVoiceRecognition)
                return;

            if (m_MicrophoneStream != null)
                m_MicrophoneStream.OnPcmChunk += OnPcmChunk;
        }

        void OnDisable()
        {
            if (m_DisableVoiceRecognition)
                return;

            if (m_MicrophoneStream != null)
                m_MicrophoneStream.OnPcmChunk -= OnPcmChunk;
        }

        void Start()
        {
            if (m_DisableVoiceRecognition)
                return;

            StartCoroutine(InitializeEngine());
        }

        void OnDestroy()
        {
            m_Recognizer?.Dispose();
            m_Recognizer = null;
            m_Model?.Dispose();
            m_Model = null;
        }

        public void SetListeningEnabled(bool enabled)
        {
            if (m_DisableVoiceRecognition)
                return;

            m_ListeningEnabled = enabled;
            m_Detector?.Reset();
            m_Panel?.SetStatus(enabled ? "语音口令：聆听中" : "语音口令：已暂停");
        }

        public void ClearLog()
        {
            if (m_DisableVoiceRecognition)
                return;

            m_Panel?.Clear();
            m_Panel?.SetStatus("语音口令：记录已清空");
        }

        IEnumerator InitializeEngine()
        {
            if (m_IsInitializing)
                yield break;

            m_IsInitializing = true;
            m_Panel.SetStatus("语音口令：加载词表...");

            try
            {
                m_Table = VoiceCommandTable.LoadFromStreamingAssets(m_CommandTableRelativePath);
                m_GrammarJson = m_Table.BuildVoskGrammarJson();
            }
            catch (Exception ex)
            {
                m_Panel.SetStatus("语音口令：词表加载失败");
                Debug.LogError($"[VoiceCmd] {ex.Message}");
                m_IsInitializing = false;
                yield break;
            }

            m_Panel.SetStatus("语音口令：准备模型...");
            yield return VoskModelLoader.EnsureModelReady(m_Table.ModelFolderName, OnModelReady);

            if (m_MicrophoneStream != null && m_StartWhenMicReady && !m_MicrophoneStream.IsCapturing)
                m_MicrophoneStream.StartCapture();

            m_IsInitializing = false;
        }

        void OnModelReady(string modelPath, string error)
        {
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(modelPath))
            {
                m_Panel.SetStatus("语音口令：模型未就绪");
                m_Panel.AddEntry(string.Empty, string.Empty, error ?? "模型路径为空", 0f);
                Debug.LogError($"[VoiceCmd] {error}");
                return;
            }

            try
            {
                m_Model?.Dispose();
                m_Recognizer?.Dispose();

                m_Model = new VoskModel(modelPath);
                var sampleRate = m_MicrophoneStream != null ? m_MicrophoneStream.SampleRate : 16000;
                m_Recognizer = new VoskGrammarRecognizer(m_Model, sampleRate, m_GrammarJson);
                m_Detector = new PcmUtteranceDetector(sampleRate,
                    m_MicrophoneStream != null ? m_MicrophoneStream.Channels : 1,
                    m_MicrophoneStream != null ? m_MicrophoneStream.BytesPerSample : 2);

                m_IsReady = true;
                m_Panel.SetStatus($"语音口令：就绪（{m_Table.Data.commands.Length} 条）");
                Debug.Log($"[VoiceCmd] Vosk ready. Model={modelPath}, grammar phrases={m_Table.AllPhrases.Count}");
            }
            catch (DllNotFoundException ex)
            {
                m_IsReady = false;
                m_Panel.SetStatus("语音口令：缺少 libvosk");
                m_Panel.AddEntry(string.Empty, string.Empty, ex.Message, 0f);
                Debug.LogError($"[VoiceCmd] {ex}");
            }
            catch (Exception ex)
            {
                m_IsReady = false;
                m_Panel.SetStatus("语音口令：引擎初始化失败");
                m_Panel.AddEntry(string.Empty, string.Empty, ex.Message, 0f);
                Debug.LogError($"[VoiceCmd] {ex}");
            }
        }

        void Update()
        {
            if (m_DisableVoiceRecognition)
                return;

            if (!TryDequeueUtterance(out var utterance))
                return;

            RecognizeUtterance(utterance);
        }

        bool TryDequeueUtterance(out byte[] utterance)
        {
            lock (m_UtteranceLock)
            {
                if (m_PendingUtterances.Count == 0)
                {
                    utterance = null;
                    return false;
                }

                utterance = m_PendingUtterances.Dequeue();
                return true;
            }
        }

        void OnPcmChunk(XREALAudioPcmChunk chunk)
        {
            if (!m_IsReady || !m_ListeningEnabled || m_Detector == null || m_Recognizer == null)
                return;

            m_Detector.Push(chunk.Data, chunk.Data.Length, EnqueueUtterance);
        }

        void EnqueueUtterance(byte[] utterancePcm)
        {
            if (utterancePcm == null || utterancePcm.Length == 0)
                return;

            lock (m_UtteranceLock)
            {
                while (m_PendingUtterances.Count >= MaxPendingUtterances)
                    m_PendingUtterances.Dequeue();

                m_PendingUtterances.Enqueue(utterancePcm);
            }
        }

        void RecognizeUtterance(byte[] utterancePcm)
        {
            if (utterancePcm == null || utterancePcm.Length < 640)
                return;

            try
            {
                m_Recognizer.Reset();
                m_Recognizer.AcceptWaveform(utterancePcm, utterancePcm.Length);
                var json = m_Recognizer.FinalResultJson();
                var text = VoskResultParser.ExtractText(json);

                if (string.IsNullOrWhiteSpace(text) || text == "[unk]")
                {
                    m_Panel.AddEntry(text, "-", "未识别", 0f);
                    return;
                }

                if (!m_Table.TryMatch(text, out var entry, out var phrase, out var score))
                {
                    m_Panel.AddEntry(text, "-", "拒识", score);
                    Debug.Log($"[VoiceCmd] Rejected raw=\"{text}\" score={score:0.00}");
                    return;
                }

                m_Panel.AddEntry(text, entry.displayName, "命中", score);

                if (Time.unscaledTime < m_NextAllowedDispatchTime)
                    return;

                m_NextAllowedDispatchTime = Time.unscaledTime + m_Table.CooldownSeconds;
                var executed = m_CommandBridge != null && m_CommandBridge.TryExecute(entry.action);
                m_Panel.AddEntry(text, entry.displayName, executed ? "已执行" : "执行失败", score);
                Debug.Log($"[VoiceCmd] Executed id={entry.id} action={entry.action} phrase=\"{phrase}\" score={score:0.00}");
            }
            catch (Exception ex)
            {
                m_Panel.AddEntry(string.Empty, string.Empty, $"识别异常: {ex.Message}", 0f);
                Debug.LogWarning($"[VoiceCmd] RecognizeUtterance failed: {ex}");
            }
        }

        void OnGUI()
        {
            if (m_DisableVoiceRecognition || !m_ShowBeamProPanel)
                return;

            m_Panel?.Draw();
        }
    }
}
