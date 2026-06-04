using System;

namespace Vosk
{
    public sealed class VoskModel : IDisposable
    {
        IntPtr m_Handle;

        public VoskModel(string modelPath)
        {
            VoskNative.vosk_set_log_level(-1);
            m_Handle = VoskNative.vosk_model_new(modelPath);
            if (m_Handle == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load Vosk model at: {modelPath}");
        }

        internal IntPtr Handle => m_Handle;

        public void Dispose()
        {
            if (m_Handle == IntPtr.Zero)
                return;

            VoskNative.vosk_model_free(m_Handle);
            m_Handle = IntPtr.Zero;
        }
    }
}
