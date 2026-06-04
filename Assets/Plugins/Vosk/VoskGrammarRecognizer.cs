using System;

namespace Vosk
{
    public sealed class VoskGrammarRecognizer : IDisposable
    {
        IntPtr m_Handle;
        bool m_Disposed;
        readonly float m_SampleRate;

        public VoskGrammarRecognizer(VoskModel model, float sampleRate, string grammarJson)
        {
            m_SampleRate = sampleRate;
            m_Handle = VoskNative.vosk_recognizer_new_grm(model.Handle, sampleRate, grammarJson);
            if (m_Handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create Vosk grammar recognizer.");
        }

        public float SampleRate => m_SampleRate;

        public bool AcceptWaveform(byte[] pcm16Le, int byteLength)
        {
            return VoskNative.vosk_recognizer_accept_waveform(m_Handle, pcm16Le, byteLength) != 0;
        }

        public string ResultJson()
        {
            return VoskNative.PtrToStringUtf8(VoskNative.vosk_recognizer_result(m_Handle));
        }

        public string FinalResultJson()
        {
            return VoskNative.PtrToStringUtf8(VoskNative.vosk_recognizer_final_result(m_Handle));
        }

        public void Reset()
        {
            VoskNative.vosk_recognizer_reset(m_Handle);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
            if (m_Handle != IntPtr.Zero)
            {
                VoskNative.vosk_recognizer_free(m_Handle);
                m_Handle = IntPtr.Zero;
            }
        }
    }
}
