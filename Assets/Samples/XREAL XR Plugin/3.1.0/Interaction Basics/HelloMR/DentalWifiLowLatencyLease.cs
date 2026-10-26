using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>Main-thread-owned lease; repeated frame updates never repeat Android calls.</summary>
    public sealed class DentalWifiLowLatencyLease : IDisposable
    {
        public interface ILock : IDisposable
        {
            bool TryAcquire();
            bool TryRelease();
        }

        readonly ILock m_Lock;
        bool m_Requested;
        bool m_Held;
        bool m_Disposed;

        public DentalWifiLowLatencyLease() : this(new AndroidWifiLock()) { }

        public DentalWifiLowLatencyLease(ILock wifiLock)
        {
            m_Lock = wifiLock ?? throw new ArgumentNullException(nameof(wifiLock));
        }

        public void SetRequired(bool required)
        {
            if (m_Disposed || m_Requested == required)
                return;
            m_Requested = required;
            if (required)
            {
                if (!m_Held)
                    m_Held = m_Lock.TryAcquire();
            }
            else
                m_Held = !m_Lock.TryRelease();
        }

        // Explicit lifecycle events retry a failed cleanup. SetRequired's frame polling
        // remains idempotent, including after a release failure.
        public void Release()
        {
            if (m_Disposed)
                return;
            m_Requested = false;
            m_Held = !m_Lock.TryRelease();
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            Release();
            m_Lock.Dispose();
            m_Disposed = true;
        }

        sealed class AndroidWifiLock : ILock
        {
            bool m_Unavailable;
            bool m_FailureLogged;
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject m_WifiLock;
            bool m_MayBeHeld;
#endif

            public bool TryAcquire()
            {
                if (m_Unavailable)
                    return false;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    if (m_WifiLock == null)
                    {
                        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        {
                            var sdk = version.GetStatic<int>("SDK_INT");
                            if (sdk < 29)
                            {
                                m_Unavailable = true;
                                Debug.Log($"DentalWifiLatency: unsupported API {sdk}; requires 29+");
                                return false;
                            }
                        }
                        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                        using (var manager = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
                        {
                            // WIFI_MODE_FULL_LOW_LATENCY. This does not change Wi-Fi settings.
                            m_WifiLock = manager.Call<AndroidJavaObject>("createWifiLock", 4,
                                "DentalNavigation.LowLatency");
                            m_WifiLock.Call("setReferenceCounted", false);
                        }
                    }
                    m_MayBeHeld = true;
                    m_WifiLock.Call("acquire");
                    if (!m_WifiLock.Call<bool>("isHeld"))
                        throw new InvalidOperationException("WifiLock acquire returned without holding the lock.");
                    Debug.Log("DentalWifiLatency: acquired mode=4 tag=DentalNavigation.LowLatency");
                    return true;
                }
                catch (Exception exception)
                {
                    Failure(exception);
                    TryRelease(); // Also covers an acquire that failed after creating/holding the lock.
                    return false;
                }
#else
                m_Unavailable = true;
                Debug.Log("DentalWifiLatency: unsupported non-Android player");
                return false;
#endif
            }

            public bool TryRelease()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (m_WifiLock == null || !m_MayBeHeld)
                    return true;
                try
                {
                    // Non-reference-counted release is safe to repeat. Do not let a
                    // separate isHeld JNI failure prevent the actual release call.
                    m_WifiLock.Call("release");
                    m_MayBeHeld = false;
                    Debug.Log("DentalWifiLatency: released tag=DentalNavigation.LowLatency");
                }
                catch (Exception exception)
                {
                    Failure(exception);
                    return false;
                }
#endif
                return true;
            }

            public void Dispose()
            {
                TryRelease();
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    m_WifiLock?.Dispose();
                }
                catch (Exception exception)
                {
                    Failure(exception);
                }
                finally
                {
                    m_WifiLock = null;
                }
#endif
                m_Unavailable = true;
            }

            void Failure(Exception exception)
            {
                m_Unavailable = true;
                if (m_FailureLogged)
                    return;
                m_FailureLogged = true;
                Debug.LogWarning($"DentalWifiLatency: failure {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
