using System;
using System.Runtime.InteropServices;

namespace Vosk
{
    internal static class VoskNative
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string LibName = "vosk";
#else
        const string LibName = "libvosk";
#endif

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_set_log_level(int log_level);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_model_new(string model_path);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_model_free(IntPtr model);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_new(IntPtr model, float sample_rate);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float sample_rate, string grammar);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_free(IntPtr recognizer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vosk_recognizer_accept_waveform(IntPtr recognizer, byte[] data, int len);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_result(IntPtr recognizer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_reset(IntPtr recognizer);

        public static string PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return string.Empty;

            var len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
                len++;

            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
