using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Adapter boundary for an offline hand-landmark implementation. A native MediaPipe
    /// Android bridge can register a factory before the scene loads without changing the
    /// recognizer or RGB camera owner.
    /// </summary>
    public interface IRgbHandLandmarkProvider : IDisposable
    {
        string Name { get; }
        string Status { get; }
        bool IsAvailable { get; }
        bool IsRunning { get; }
        event Action<RgbHandLandmarkFrame> LandmarksReady;
        bool Start();
        void Stop();
        bool TrySubmitFrame(RgbCameraFrame frame);
    }

    public readonly struct RgbHandLandmarkFrame
    {
        public const int LandmarkCount = 21;

        public RgbHandLandmarkFrame(
            Vector3[] landmarks,
            float confidence,
            bool isTracked,
            long sourceSequence,
            double observedAtSeconds)
        {
            Landmarks = landmarks;
            Confidence = confidence;
            IsTracked = isTracked;
            SourceSequence = sourceSequence;
            ObservedAtSeconds = observedAtSeconds;
        }

        public Vector3[] Landmarks { get; }
        public float Confidence { get; }
        public bool IsTracked { get; }
        public long SourceSequence { get; }
        public double ObservedAtSeconds { get; }
        public bool IsValid => IsTracked && Landmarks != null && Landmarks.Length >= LandmarkCount;

        public static RgbHandLandmarkFrame NotTracked(long sourceSequence, double observedAtSeconds)
        {
            return new RgbHandLandmarkFrame(null, 0f, false, sourceSequence, observedAtSeconds);
        }
    }

    public static class RgbHandLandmarkProviderRegistry
    {
        static readonly object s_Lock = new object();
        static Func<IRgbHandLandmarkProvider> s_Factory;

        public static void RegisterFactory(Func<IRgbHandLandmarkProvider> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            lock (s_Lock)
                s_Factory = factory;
        }

        public static void ClearFactory()
        {
            lock (s_Lock)
                s_Factory = null;
        }

        public static IRgbHandLandmarkProvider CreateProvider()
        {
            Func<IRgbHandLandmarkProvider> factory;
            lock (s_Lock)
                factory = s_Factory;

            if (factory == null)
                return new UnavailableRgbHandLandmarkProvider();

            try
            {
                return factory() ?? new UnavailableRgbHandLandmarkProvider("Registered landmark factory returned null.");
            }
            catch (Exception ex)
            {
                return new UnavailableRgbHandLandmarkProvider($"Landmark provider creation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Honest platform/build fallback. Image heuristics are deliberately not used as a
    /// substitute when the Android MediaPipe bridge cannot be selected.
    /// </summary>
    public sealed class UnavailableRgbHandLandmarkProvider : IRgbHandLandmarkProvider
    {
        readonly string m_Status;

        public UnavailableRgbHandLandmarkProvider(
            string status = "MediaPipe Android landmark provider is unavailable on this platform/build.")
        {
            m_Status = status;
        }

        public string Name => "Unavailable";
        public string Status => m_Status;
        public bool IsAvailable => false;
        public bool IsRunning => false;
        public event Action<RgbHandLandmarkFrame> LandmarksReady { add { } remove { } }
        public bool Start() => false;
        public void Stop() { }
        public bool TrySubmitFrame(RgbCameraFrame frame) => false;
        public void Dispose() { }
    }

    /// <summary>
    /// Deterministic provider for editor tests and recorded landmark playback. It never
    /// interprets pixels and is not selected automatically in a player build.
    /// </summary>
    public sealed class ScriptedRgbHandLandmarkProvider : IRgbHandLandmarkProvider
    {
        readonly Queue<RgbHandLandmarkFrame> m_Frames = new Queue<RgbHandLandmarkFrame>();
        bool m_Running;

        public string Name => "Scripted test provider";
        public string Status => m_Running ? "running" : "stopped";
        public bool IsAvailable => true;
        public bool IsRunning => m_Running;
        public event Action<RgbHandLandmarkFrame> LandmarksReady;

        public void Enqueue(RgbHandLandmarkFrame frame)
        {
            lock (m_Frames)
                m_Frames.Enqueue(frame);
        }

        public bool Start()
        {
            m_Running = true;
            return true;
        }

        public void Stop()
        {
            m_Running = false;
        }

        public bool TrySubmitFrame(RgbCameraFrame frame)
        {
            if (!m_Running)
                return false;

            RgbHandLandmarkFrame next;
            lock (m_Frames)
            {
                if (m_Frames.Count == 0)
                    return false;
                next = m_Frames.Dequeue();
            }

            LandmarksReady?.Invoke(next);
            return true;
        }

        public void Dispose()
        {
            Stop();
            lock (m_Frames)
                m_Frames.Clear();
        }
    }
}
