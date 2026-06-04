using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    /// <summary>
    /// Buffers PCM and emits utterances after speech + trailing silence (energy VAD).
    /// </summary>
    public sealed class PcmUtteranceDetector
    {
        readonly List<byte> m_Utterance = new List<byte>(48000);
        readonly int m_BytesPerSample;
        readonly int m_Channels;
        readonly int m_SampleRate;
        readonly int m_FrameBytes;
        readonly float m_SpeechStartThreshold;
        readonly float m_SpeechContinueThreshold;
        readonly int m_MinSpeechMs;
        readonly int m_EndSilenceMs;
        readonly int m_MaxUtteranceMs;

        readonly byte[] m_FrameScratch;
        int m_ScratchOffset;

        bool m_InSpeech;
        int m_SpeechMs;
        int m_SilenceMs;

        public PcmUtteranceDetector(int sampleRate, int channels, int bytesPerSample,
            float speechStartThreshold = 0.012f,
            float speechContinueThreshold = 0.008f,
            int minSpeechMs = 280,
            int endSilenceMs = 650,
            int maxUtteranceMs = 3500)
        {
            m_SampleRate = sampleRate;
            m_Channels = channels;
            m_BytesPerSample = bytesPerSample;
            m_SpeechStartThreshold = speechStartThreshold;
            m_SpeechContinueThreshold = speechContinueThreshold;
            m_MinSpeechMs = minSpeechMs;
            m_EndSilenceMs = endSilenceMs;
            m_MaxUtteranceMs = maxUtteranceMs;
            m_FrameBytes = Mathf.Max(320, sampleRate * channels * bytesPerSample / 20);
            m_FrameScratch = new byte[m_FrameBytes];
        }

        public void Push(byte[] pcmChunk, int byteCount, Action<byte[]> onUtteranceReady)
        {
            if (pcmChunk == null || byteCount <= 0)
                return;

            var offset = 0;
            while (offset < byteCount)
            {
                var copy = Mathf.Min(m_FrameBytes - m_ScratchOffset, byteCount - offset);
                Buffer.BlockCopy(pcmChunk, offset, m_FrameScratch, m_ScratchOffset, copy);
                m_ScratchOffset += copy;
                offset += copy;

                if (m_ScratchOffset < m_FrameBytes)
                    continue;

                ProcessFrame(m_FrameScratch, onUtteranceReady);
                m_ScratchOffset = 0;
            }
        }

        void ProcessFrame(byte[] frame, Action<byte[]> onUtteranceReady)
        {
            var frameMs = Mathf.RoundToInt(1000f * frame.Length / (m_SampleRate * m_Channels * m_BytesPerSample));
            var rms = ComputeRms(frame);

            if (!m_InSpeech)
            {
                if (rms < m_SpeechStartThreshold)
                    return;

                m_InSpeech = true;
                m_SpeechMs = 0;
                m_SilenceMs = 0;
                m_Utterance.Clear();
            }

            m_Utterance.AddRange(frame);
            m_SpeechMs += frameMs;
            m_SilenceMs = rms < m_SpeechContinueThreshold ? m_SilenceMs + frameMs : 0;

            var shouldFinalize = m_SpeechMs >= m_MinSpeechMs &&
                                 (m_SilenceMs >= m_EndSilenceMs || m_SpeechMs >= m_MaxUtteranceMs);

            if (!shouldFinalize)
                return;

            if (m_Utterance.Count > 0)
                onUtteranceReady?.Invoke(m_Utterance.ToArray());

            m_Utterance.Clear();
            m_InSpeech = false;
            m_SpeechMs = 0;
            m_SilenceMs = 0;
        }

        public void Reset()
        {
            m_Utterance.Clear();
            m_ScratchOffset = 0;
            m_InSpeech = false;
            m_SpeechMs = 0;
            m_SilenceMs = 0;
        }

        static float ComputeRms(byte[] frame)
        {
            if (frame.Length < 2)
                return 0f;

            double sum = 0;
            var count = 0;
            for (var i = 0; i + 1 < frame.Length; i += 2)
            {
                var sample = (short)(frame[i] | (frame[i + 1] << 8));
                var normalized = sample / 32768f;
                sum += normalized * normalized;
                count++;
            }

            return count == 0 ? 0f : Mathf.Sqrt((float)(sum / count));
        }
    }
}
