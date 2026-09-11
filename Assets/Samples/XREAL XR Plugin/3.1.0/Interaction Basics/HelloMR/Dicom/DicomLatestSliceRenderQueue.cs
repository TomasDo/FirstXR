using System;
using System.Threading.Tasks;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>CPU-only renderer contract. Implementations must not create or mutate Unity objects.</summary>
    public interface IDicomSlicePixelRenderer
    {
        byte[] Render(DicomVolume volume, DicomSliceRequest request, Func<bool> isStale);
    }

    public sealed class CachedDicomSlicePixelRenderer : IDicomSlicePixelRenderer
    {
        readonly DicomCpuSliceCache m_Cache;

        public CachedDicomSlicePixelRenderer(int cacheCapacity)
        {
            m_Cache = new DicomCpuSliceCache(cacheCapacity);
        }

        public byte[] Render(DicomVolume volume, DicomSliceRequest request, Func<bool> isStale)
        {
            return m_Cache.GetOrCreate(volume, request, isStale);
        }
    }

    public readonly struct DicomSlicePixelResult
    {
        public readonly ulong RequestVersion;
        public readonly DicomVolume Volume;
        public readonly DicomSliceRequest Request;
        public readonly byte[] Pixels;
        public readonly string Error;

        public bool Success => Pixels != null && string.IsNullOrEmpty(Error);

        public DicomSlicePixelResult(
            ulong requestVersion,
            DicomVolume volume,
            DicomSliceRequest request,
            byte[] pixels,
            string error)
        {
            RequestVersion = requestVersion;
            Volume = volume;
            Request = request;
            Pixels = pixels;
            Error = error ?? string.Empty;
        }
    }

    /// <summary>
    /// Bounded latest-only CPU slice queue. It owns one worker, one replaceable pending request and
    /// one replaceable completion. Superseded renders stop at the next scanline cancellation check.
    /// Consumers poll TryTakeLatest on Unity's main thread and create Texture2D there.
    /// </summary>
    public sealed class DicomLatestSliceRenderQueue : IDisposable
    {
        readonly object m_Gate = new object();
        readonly IDicomSlicePixelRenderer m_Renderer;
        WorkItem m_Pending;
        DicomSlicePixelResult? m_Completed;
        ulong m_LatestVersion;
        bool m_WorkerRunning;
        bool m_Disposed;

        public DicomLatestSliceRenderQueue(int cacheCapacity = 8, IDicomSlicePixelRenderer renderer = null)
        {
            if (cacheCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
            m_Renderer = renderer ?? new CachedDicomSlicePixelRenderer(cacheCapacity);
        }

        public ulong Request(DicomVolume volume, DicomSliceRequest request)
        {
            if (volume == null)
                throw new ArgumentNullException(nameof(volume));

            lock (m_Gate)
            {
                ThrowIfDisposed();
                var version = NextVersion();
                m_Pending = new WorkItem(version, volume, request);
                m_Completed = null;
                if (!m_WorkerRunning)
                {
                    m_WorkerRunning = true;
                    _ = Task.Run(WorkerLoop);
                }
                return version;
            }
        }

        /// <summary>Cancels pending/stale delivery without waiting for the current scanline loop.</summary>
        public ulong Invalidate()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return m_LatestVersion;
                var version = NextVersion();
                m_Pending = null;
                m_Completed = null;
                return version;
            }
        }

        public bool TryTakeLatest(out DicomSlicePixelResult result)
        {
            lock (m_Gate)
            {
                if (m_Completed.HasValue && m_Completed.Value.RequestVersion == m_LatestVersion)
                {
                    result = m_Completed.Value;
                    m_Completed = null;
                    return true;
                }
            }
            result = default;
            return false;
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_LatestVersion++;
                m_Pending = null;
                m_Completed = null;
            }
        }

        void WorkerLoop()
        {
            while (true)
            {
                WorkItem work;
                lock (m_Gate)
                {
                    if (m_Disposed || m_Pending == null)
                    {
                        m_WorkerRunning = false;
                        return;
                    }
                    work = m_Pending;
                    m_Pending = null;
                }

                DicomSlicePixelResult result;
                try
                {
                    var pixels = m_Renderer.Render(work.Volume, work.Request, () => IsStale(work.Version));
                    result = new DicomSlicePixelResult(work.Version, work.Volume, work.Request, pixels, string.Empty);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception exception)
                {
                    result = new DicomSlicePixelResult(work.Version, work.Volume, work.Request, null, exception.Message);
                }

                lock (m_Gate)
                {
                    if (!m_Disposed && work.Version == m_LatestVersion)
                        m_Completed = result;
                }
            }
        }

        bool IsStale(ulong version)
        {
            lock (m_Gate)
                return m_Disposed || version != m_LatestVersion;
        }

        ulong NextVersion()
        {
            m_LatestVersion++;
            if (m_LatestVersion == 0)
                m_LatestVersion = 1;
            return m_LatestVersion;
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(DicomLatestSliceRenderQueue));
        }

        sealed class WorkItem
        {
            public readonly ulong Version;
            public readonly DicomVolume Volume;
            public readonly DicomSliceRequest Request;

            public WorkItem(ulong version, DicomVolume volume, DicomSliceRequest request)
            {
                Version = version;
                Volume = volume;
                Request = request;
            }
        }
    }
}
