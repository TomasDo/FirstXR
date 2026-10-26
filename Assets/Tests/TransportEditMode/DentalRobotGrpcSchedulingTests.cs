using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalRobotGrpcSchedulingTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task BufferedReadPreservesMessageAndEndOfStream(bool hasMessage)
        {
            Assert.That(await AwaitMessage(Task.FromResult(hasMessage), TimeSpan.FromSeconds(30)),
                Is.EqualTo(hasMessage));
        }

        [Test]
        public async Task PendingReadCompletesWithoutWaitingForInactivityTimeout()
        {
            var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = AwaitMessage(read.Task, TimeSpan.FromSeconds(30));
            Assert.That(result.IsCompleted, Is.False);
            read.SetResult(true);
            Assert.That(await CompletePromptly(result), Is.True);
        }

        [Test]
        public async Task InactiveReadReportsTimeout()
        {
            var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await CompletePromptly(AwaitMessage(read.Task, TimeSpan.FromMilliseconds(10)));
                Assert.Fail("An inactive stream must time out.");
            }
            catch (TimeoutException exception)
            {
                Assert.That(exception.Message, Is.EqualTo("inactivity probe"));
            }
            finally
            {
                read.TrySetResult(false);
            }
        }

        [Test]
        public async Task DisconnectCancellationIsNotReportedAsInactivity()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var result = AwaitMessage(read.Task, TimeSpan.FromSeconds(30), cancellation.Token);
                cancellation.Cancel();
                try
                {
                    await CompletePromptly(result);
                    Assert.Fail("Disconnect must cancel the pending wait.");
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    read.TrySetResult(false);
                }
            }
        }

        [Test]
        public async Task StreamTimerStopsDuringHandlingAndCanBeRearmedForNextRead()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var result = InvokeReadTimeout(new TaskStreamReader(read.Task), cancellation,
                    TimeSpan.FromMilliseconds(200), CancellationToken.None);
                read.SetResult(true);
                Assert.That(await CompletePromptly(result), Is.True);
                await Task.Delay(400);
                Assert.That(cancellation.IsCancellationRequested, Is.False,
                    "The inactivity deadline must stop while a message handler applies backpressure.");

                var nextRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await CompletePromptly(InvokeReadTimeout(new TaskStreamReader(nextRead.Task), cancellation,
                        TimeSpan.FromMilliseconds(10), CancellationToken.None));
                    Assert.Fail("The same stream timer must enforce the next read deadline.");
                }
                catch (TimeoutException)
                {
                    Assert.That(cancellation.IsCancellationRequested, Is.True);
                }
            }
        }

        [Test]
        public async Task GrpcCancelledStatusIsTranslatedOnlyForAnExpiredDeadline()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    await CompletePromptly(InvokeReadTimeout(new GrpcCancellationReader(), cancellation,
                        TimeSpan.FromMilliseconds(10), CancellationToken.None));
                    Assert.Fail("gRPC reports read cancellation as a Cancelled status.");
                }
                catch (TimeoutException exception)
                {
                    Assert.That(exception.Message, Is.EqualTo("inactivity probe"));
                }
            }
        }

        [Test]
        public async Task CancelledWriteDuringCleanupDoesNotMaskReadTimeout()
        {
            var stream = new FaultingRequestStream();
            var writer = CreateRequestWriter(stream);
            try
            {
                EnqueueWrite(writer);
                await CompletePromptly(stream.WriteStarted.Task);
                stream.Fail(StatusCode.Cancelled);
                try
                {
                    try
                    {
                        throw new TimeoutException("read deadline");
                    }
                    finally
                    {
                        await StopWriter(writer, stream);
                    }
                }
                catch (TimeoutException exception)
                {
                    Assert.That(exception.Message, Is.EqualTo("read deadline"));
                }
                Assert.That(stream.Completed, Is.True);
                AssertWriterDisposed(writer);
            }
            finally
            {
                ((IDisposable)writer).Dispose();
            }
        }

        [Test]
        public async Task UnexpectedWriteFailureStillDisposesWriter()
        {
            var stream = new FaultingRequestStream();
            var writer = CreateRequestWriter(stream);
            try
            {
                EnqueueWrite(writer);
                await CompletePromptly(stream.WriteStarted.Task);
                stream.Fail(StatusCode.Unavailable);
                try
                {
                    await StopWriter(writer, stream);
                    Assert.Fail("Non-cancellation transport errors must remain visible.");
                }
                catch (RpcException exception)
                {
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCode.Unavailable));
                }
                AssertWriterDisposed(writer);
            }
            finally
            {
                ((IDisposable)writer).Dispose();
            }
        }

        [Test]
        public void DispatchYieldsAfterExpensiveActionAndPreservesFollowingWork()
        {
            WithInactiveClient(client =>
            {
                SetField(client, "m_MaxMainThreadDispatchMilliseconds", 1f);
                var calls = new List<int>();
                Enqueue(client, () =>
                {
                    calls.Add(1);
                    Thread.Sleep(10);
                });
                Enqueue(client, () => calls.Add(2));

                Drain(client);
                CollectionAssert.AreEqual(new[] { 1 }, calls,
                    "An expensive asset callback must yield before the next callback.");
                Drain(client);
                CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
            });
        }

        [Test]
        public void DispatchStillHonorsActionCountLimit()
        {
            WithInactiveClient(client =>
            {
                SetField(client, "m_MaxMainThreadActionsPerFrame", 1);
                SetField(client, "m_MaxMainThreadDispatchMilliseconds", 1000f);
                var count = 0;
                Enqueue(client, () => count++);
                Enqueue(client, () => count++);
                Drain(client);
                Assert.That(count, Is.EqualTo(1));
                Drain(client);
                Assert.That(count, Is.EqualTo(2));
            });
        }

        static async Task<bool> AwaitMessage(Task<bool> read, TimeSpan timeout,
            CancellationToken cancellation = default)
        {
            using (var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                return await InvokeReadTimeout(new TaskStreamReader(read), inactivity, timeout, cancellation);
        }

        internal static Task<bool> InvokeReadTimeout(IAsyncStreamReader<byte[]> reader, CancellationTokenSource inactivity,
            TimeSpan timeout, CancellationToken cancellation)
        {
            var method = typeof(DentalRobotGrpcClient).GetMethod("MoveNextWithTimeoutAsync",
                BindingFlags.NonPublic | BindingFlags.Static)?.MakeGenericMethod(typeof(byte[]));
            Assert.That(method, Is.Not.Null);
            return (Task<bool>)method.Invoke(null,
                new object[] { reader, inactivity, timeout, "inactivity probe", cancellation });
        }

        static object CreateRequestWriter(IClientStreamWriter<byte[]> stream)
        {
            var type = typeof(DentalRobotGrpcClient).GetNestedType("AsyncRequestWriter`1", BindingFlags.NonPublic)
                ?.MakeGenericType(typeof(byte[]));
            Assert.That(type, Is.Not.Null);
            return Activator.CreateInstance(type, stream, 2, CancellationToken.None);
        }

        static void EnqueueWrite(object writer)
        {
            Assert.That((bool)writer.GetType().GetMethod("TryEnqueue")
                .Invoke(writer, new object[] { new byte[] { 1 } }), Is.True);
        }

        static Task StopWriter(object writer, IClientStreamWriter<byte[]> stream)
        {
            var method = typeof(DentalRobotGrpcClient).GetMethod("StopAndDisposeWriterAsync",
                BindingFlags.NonPublic | BindingFlags.Static)?.MakeGenericMethod(typeof(byte[]));
            Assert.That(method, Is.Not.Null);
            return (Task)method.Invoke(null, new[] { writer, stream });
        }

        static void AssertWriterDisposed(object writer)
        {
            var items = (SemaphoreSlim)writer.GetType().GetField("m_Items", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(writer);
            Assert.Throws<ObjectDisposedException>(() => items.Wait(0));
            var stop = (CancellationTokenSource)writer.GetType().GetField("m_Stop", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(writer);
            Assert.Throws<ObjectDisposedException>(() => { var ignored = stop.Token; });
        }

        sealed class FaultingRequestStream : IClientStreamWriter<byte[]>
        {
            public readonly TaskCompletionSource<bool> WriteStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> m_Write =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public WriteOptions WriteOptions { get; set; }
            public bool Completed { get; private set; }

            public Task WriteAsync(byte[] message)
            {
                WriteStarted.TrySetResult(true);
                return m_Write.Task;
            }

            public Task CompleteAsync()
            {
                Completed = true;
                return Task.CompletedTask;
            }

            public void Fail(StatusCode status) => m_Write.TrySetException(new RpcException(new Status(status, "probe")));
        }

        sealed class TaskStreamReader : IAsyncStreamReader<byte[]>
        {
            readonly Task<bool> m_Read;
            public byte[] Current => Array.Empty<byte>();

            public TaskStreamReader(Task<bool> read) => m_Read = read;

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (m_Read.IsCompleted)
                    return await m_Read;
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetCanceled(cancellationToken)))
                    return await await Task.WhenAny(m_Read, cancelled.Task);
            }
        }

        sealed class GrpcCancellationReader : IAsyncStreamReader<byte[]>
        {
            public byte[] Current => Array.Empty<byte>();

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetException(
                           new RpcException(new Status(StatusCode.Cancelled, "read cancelled")))))
                    return await cancelled.Task;
            }
        }

        static async Task<bool> CompletePromptly(Task<bool> task)
        {
            using (var timeout = new CancellationTokenSource())
            {
                try
                {
                    Assert.That(await Task.WhenAny(task, Task.Delay(2000, timeout.Token)), Is.SameAs(task));
                    return await task;
                }
                finally
                {
                    timeout.Cancel();
                }
            }
        }

        static void WithInactiveClient(Action<DentalRobotGrpcClient> test)
        {
            var host = new GameObject("gRPC scheduling probe");
            host.SetActive(false);
            try
            {
                test(host.AddComponent<DentalRobotGrpcClient>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        static void SetField(DentalRobotGrpcClient client, string name, object value)
        {
            var field = typeof(DentalRobotGrpcClient).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            field.SetValue(client, value);
        }

        static void Enqueue(DentalRobotGrpcClient client, Action action)
        {
            var method = typeof(DentalRobotGrpcClient).GetMethod("TryEnqueueMainThread",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(client, new object[] { action }), Is.True);
        }

        static void Drain(DentalRobotGrpcClient client)
        {
            var method = typeof(DentalRobotGrpcClient).GetMethod("DrainMainThreadActions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(client, null);
        }
    }
}
