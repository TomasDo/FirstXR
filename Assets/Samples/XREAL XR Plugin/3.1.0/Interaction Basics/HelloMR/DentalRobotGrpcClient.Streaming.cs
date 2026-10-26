using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public partial class DentalRobotGrpcClient
    {
        static async Task StopAndDisposeWriterAsync<T>(AsyncRequestWriter<T> writer, IClientStreamWriter<T> stream)
        {
            try
            {
                await writer.StopAsync().ConfigureAwait(false);
                try
                {
                    await stream.CompleteAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
            finally
            {
                // A transport failure during shutdown must not retain the linked token or semaphores.
                writer.Dispose();
            }
        }

        static async Task<bool> MoveNextWithTimeoutAsync<T>(
            IAsyncStreamReader<T> reader,
            CancellationTokenSource inactivityCancellation,
            TimeSpan timeout,
            string timeoutMessage,
            CancellationToken cancellationToken)
        {
            try
            {
                var moveNext = reader.MoveNext(inactivityCancellation.Token);
                if (!moveNext.IsCompleted)
                    inactivityCancellation.CancelAfter(timeout);
                return await moveNext.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                inactivityCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Cancelled &&
                inactivityCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage);
            }
            finally
            {
                // One linked source/timer belongs to the whole stream. Re-arm it for each
                // pending read, and stop it while handlers apply backpressure. Creating a
                // Task.Delay per message retains timers until their original due time on Mono.
                if (!inactivityCancellation.IsCancellationRequested)
                    inactivityCancellation.CancelAfter(Timeout.Infinite);
            }
        }

        sealed class BoundedDispatchQueue : IDisposable
        {
            readonly ConcurrentQueue<Action> m_Queue = new ConcurrentQueue<Action>();
            readonly SemaphoreSlim m_Items = new SemaphoreSlim(0);
            readonly SemaphoreSlim m_Spaces;
            readonly CancellationTokenSource m_Disposed = new CancellationTokenSource();
            volatile bool m_IsDisposed;

            public BoundedDispatchQueue(int capacity)
            {
                m_Spaces = new SemaphoreSlim(Math.Max(1, capacity), Math.Max(1, capacity));
            }

            public bool TryEnqueue(Action action)
            {
                if (action == null || m_IsDisposed || !m_Spaces.Wait(0))
                    return false;

                if (m_IsDisposed)
                {
                    m_Spaces.Release();
                    return false;
                }

                m_Queue.Enqueue(action);
                m_Items.Release();
                return true;
            }

            public async Task EnqueueAsync(Action action, CancellationToken cancellationToken)
            {
                if (action == null)
                    return;

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_Disposed.Token))
                {
                await m_Spaces.WaitAsync(linked.Token).ConfigureAwait(false);
                if (m_IsDisposed)
                {
                    m_Spaces.Release();
                    throw new OperationCanceledException(linked.Token);
                }
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (linked.Token.Register(() => completion.TrySetCanceled(linked.Token)))
                {
                m_Queue.Enqueue(() =>
                {
                    if (completion.Task.IsCompleted)
                        return;
                    try
                    {
                        action();
                        completion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        completion.TrySetException(ex);
                    }
                });
                m_Items.Release();
                await completion.Task.ConfigureAwait(false);
                }
                }
            }

            public async Task<bool> EnqueueAsync(Func<bool> action, CancellationToken cancellationToken)
            {
                if (action == null)
                    return false;

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_Disposed.Token))
                {
                await m_Spaces.WaitAsync(linked.Token).ConfigureAwait(false);
                if (m_IsDisposed)
                {
                    m_Spaces.Release();
                    throw new OperationCanceledException(linked.Token);
                }
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (linked.Token.Register(() => completion.TrySetCanceled(linked.Token)))
                {
                m_Queue.Enqueue(() =>
                {
                    if (completion.Task.IsCompleted)
                        return;
                    try
                    {
                        completion.TrySetResult(action());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        completion.TrySetResult(false);
                    }
                });
                m_Items.Release();
                return await completion.Task.ConfigureAwait(false);
                }
                }
            }

            public bool TryDequeue(out Action action)
            {
                action = null;
                if (!m_Items.Wait(0))
                    return false;

                if (!m_Queue.TryDequeue(out action))
                {
                    m_Spaces.Release();
                    return false;
                }

                m_Spaces.Release();
                return true;
            }

            public void Dispose()
            {
                if (m_IsDisposed)
                    return;
                m_IsDisposed = true;
                m_Disposed.Cancel();
                while (m_Items.Wait(0))
                {
                    m_Queue.TryDequeue(out _);
                    m_Spaces.Release();
                }
            }
        }

        sealed class AsyncRequestWriter<T> : IDisposable
        {
            readonly IClientStreamWriter<T> m_Stream;
            readonly ConcurrentQueue<T> m_Queue = new ConcurrentQueue<T>();
            readonly SemaphoreSlim m_Items = new SemaphoreSlim(0);
            readonly SemaphoreSlim m_Spaces;
            readonly CancellationTokenSource m_Stop;
            readonly Task m_WriterTask;

            public AsyncRequestWriter(IClientStreamWriter<T> stream, int capacity, CancellationToken cancellationToken)
            {
                m_Stream = stream;
                m_Spaces = new SemaphoreSlim(Math.Max(1, capacity), Math.Max(1, capacity));
                m_Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                m_WriterTask = WriteLoopAsync(m_Stop.Token);
            }

            public bool TryEnqueue(T message)
            {
                if (message == null || m_WriterTask.IsCompleted || !m_Spaces.Wait(0))
                    return false;

                m_Queue.Enqueue(message);
                m_Items.Release();
                return true;
            }

            public async Task EnqueueAsync(T message, CancellationToken cancellationToken)
            {
                if (message == null)
                    return;

                if (m_WriterTask.IsCompleted)
                {
                    await m_WriterTask.ConfigureAwait(false);
                    throw new InvalidOperationException("The gRPC request writer has stopped.");
                }

                var spaceTask = m_Spaces.WaitAsync(cancellationToken);
                var completed = await Task.WhenAny(spaceTask, m_WriterTask).ConfigureAwait(false);
                if (completed == m_WriterTask)
                {
                    await m_WriterTask.ConfigureAwait(false);
                    throw new InvalidOperationException("The gRPC request writer has stopped.");
                }

                await spaceTask.ConfigureAwait(false);
                m_Queue.Enqueue(message);
                m_Items.Release();
            }

            public async Task StopAsync()
            {
                m_Stop.Cancel();
                try
                {
                    await m_WriterTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
                {
                    // A read deadline cancels the whole native call, including any in-flight write.
                    // Treat that as normal shutdown so it cannot mask the read's TimeoutException.
                }
            }

            async Task WriteLoopAsync(CancellationToken cancellationToken)
            {
                while (true)
                {
                    await m_Items.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (!m_Queue.TryDequeue(out var message))
                        continue;

                    m_Spaces.Release();
                    await m_Stream.WriteAsync(message).ConfigureAwait(false);
                }
            }

            public void Dispose()
            {
                m_Stop.Dispose();
                m_Items.Dispose();
                m_Spaces.Dispose();
            }
        }

        sealed class AssetReceiveProgress
        {
            readonly object m_Lock = new object();
            readonly List<ReceiveRange> m_Ranges = new List<ReceiveRange>();

            public long NextMissingOffset
            {
                get
                {
                    lock (m_Lock)
                    {
                        long next = 0;
                        for (var i = 0; i < m_Ranges.Count; i++)
                        {
                            var range = m_Ranges[i];
                            if (range.Start > next)
                                break;
                            if (range.End > next)
                                next = range.End;
                        }
                        return next;
                    }
                }
            }

            public bool Covers(long offset, long length)
            {
                if (offset < 0 || length <= 0 || offset > long.MaxValue - length)
                    return false;

                var end = offset + length;
                lock (m_Lock)
                {
                    for (var i = 0; i < m_Ranges.Count; i++)
                    {
                        var range = m_Ranges[i];
                        if (range.Start > offset)
                            return false;
                        if (range.Start <= offset && range.End >= end)
                            return true;
                    }
                }
                return false;
            }

            public void Add(long offset, long length)
            {
                if (offset < 0 || length <= 0 || offset > long.MaxValue - length)
                    return;

                var start = offset;
                var end = offset + length;
                lock (m_Lock)
                {
                    var insertAt = 0;
                    while (insertAt < m_Ranges.Count && m_Ranges[insertAt].End < start)
                        insertAt++;

                    while (insertAt < m_Ranges.Count && m_Ranges[insertAt].Start <= end)
                    {
                        start = Math.Min(start, m_Ranges[insertAt].Start);
                        end = Math.Max(end, m_Ranges[insertAt].End);
                        m_Ranges.RemoveAt(insertAt);
                    }

                    m_Ranges.Insert(insertAt, new ReceiveRange(start, end));
                }
            }

            readonly struct ReceiveRange
            {
                public readonly long Start;
                public readonly long End;

                public ReceiveRange(long start, long end)
                {
                    Start = start;
                    End = end;
                }
            }
        }

        static class Crc32
        {
            static readonly uint[] Table = CreateTable();

            public static uint Compute(byte[] data)
            {
                var crc = 0xffffffffu;
                if (data != null)
                {
                    for (var i = 0; i < data.Length; i++)
                        crc = Table[(crc ^ data[i]) & 0xff] ^ (crc >> 8);
                }

                return crc ^ 0xffffffffu;
            }

            static uint[] CreateTable()
            {
                var table = new uint[256];
                for (uint i = 0; i < table.Length; i++)
                {
                    var value = i;
                    for (var bit = 0; bit < 8; bit++)
                        value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                    table[i] = value;
                }

                return table;
            }
        }
    }
}
