using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NUnit.Framework;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalRobotGrpcTransportTests
    {
        static readonly byte[] Request = Encoding.ASCII.GetBytes("hello");
        static readonly byte[] FramedMessage = { 0, 0, 0, 0, 5, 104, 101, 108, 108, 111 };
        static readonly Marshaller<byte[]> Bytes = Marshallers.Create<byte[]>(value => value, value => value);
        static readonly Method<byte[], byte[]> StreamingMethod = new Method<byte[], byte[]>(
            MethodType.DuplexStreaming, "transport.Probe", "Exchange", Bytes, Bytes);

        // A real loopback peer checks the native handler's wire protocol. No external server is needed.
        // Sending a request before any response headers also verifies unbuffered duplex request streaming.
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeChannelStreamsOverH2cAndStopsPendingCall(bool disposeChannel)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
                using (var cancellation = new CancellationTokenSource())
                using (var channel = DentalRobotGrpcChannelFactory.Create(address))
                using (var call = channel.CreateCallInvoker().AsyncDuplexStreamingCall(
                    StreamingMethod, null, new CallOptions(cancellationToken: cancellation.Token)))
                {
                    var response = call.ResponseStream.MoveNext(CancellationToken.None);
                    var write = call.RequestStream.WriteAsync(Request);
                    using (var peer = await AcceptPeerOrReportCallFailure(listener, response))
                    using (var wire = peer.GetStream())
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var preface = await WithinTimeout(ReadExactly(wire, 24, timeout.Token));
                        Assert.That(Encoding.ASCII.GetString(preface), Is.EqualTo("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"));

                        // An empty server SETTINGS frame is sufficient to receive the request body.
                        var settings = new byte[] { 0, 0, 0, 4, 0, 0, 0, 0, 0 };
                        await WithinTimeout(wire.WriteAsync(settings, 0, settings.Length, timeout.Token));
                        var body = await WithinTimeout(ReadRequestBody(wire, timeout.Token));
                        CollectionAssert.AreEqual(FramedMessage, body);
                        await WithinTimeout(write);
                        Assert.That(response.IsCompleted, Is.False, "The peer has not sent response headers.");

                        if (disposeChannel)
                            channel.Dispose();
                        else
                            cancellation.Cancel();

                        try
                        {
                            await WithinTimeout(response);
                            Assert.Fail("The pending streaming response must be cancelled.");
                        }
                        catch (RpcException exception)
                        {
                            Assert.That(exception.StatusCode, Is.EqualTo(StatusCode.Cancelled));
                        }
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task NativeChannelReadsStreamingResponseAndSuccessfulTrailers()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
                using (var channel = DentalRobotGrpcChannelFactory.Create(address))
                using (var call = channel.CreateCallInvoker().AsyncDuplexStreamingCall(
                    StreamingMethod, null, new CallOptions()))
                {
                    var response = call.ResponseStream.MoveNext(CancellationToken.None);
                    var write = call.RequestStream.WriteAsync(Request);
                    using (var peer = await AcceptPeerOrReportCallFailure(listener, response))
                    using (var wire = peer.GetStream())
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var preface = await WithinTimeout(ReadExactly(wire, 24, timeout.Token));
                        Assert.That(Encoding.ASCII.GetString(preface), Is.EqualTo("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"));
                        await WriteFrame(wire, 4, 0, 0, Array.Empty<byte>(), timeout.Token); // SETTINGS
                        CollectionAssert.AreEqual(FramedMessage, await WithinTimeout(ReadRequestBody(wire, timeout.Token)));
                        await WithinTimeout(write);
                        await WithinTimeout(call.RequestStream.CompleteAsync());

                        // HPACK static index 8 is :status=200. Other fields use literal, non-Huffman encoding.
                        var headers = new List<byte> { 0x88 };
                        headers.AddRange(LiteralHeader("content-type", "application/grpc"));
                        await WriteFrame(wire, 1, 4, 1, headers.ToArray(), timeout.Token); // HEADERS, END_HEADERS
                        await WriteFrame(wire, 0, 0, 1, FramedMessage, timeout.Token); // DATA
                        await WriteFrame(wire, 1, 5, 1, LiteralHeader("grpc-status", "0"), timeout.Token); // trailers, END_STREAM

                        Assert.That(await WithinTimeout(response), Is.True);
                        CollectionAssert.AreEqual(Request, call.ResponseStream.Current);
                        Assert.That(await WithinTimeout(call.ResponseStream.MoveNext(CancellationToken.None)), Is.False);
                        Assert.That(call.GetStatus().StatusCode, Is.EqualTo(StatusCode.OK));
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        static byte[] LiteralHeader(string name, string value)
        {
            var encoded = new List<byte> { 0, (byte)name.Length };
            encoded.AddRange(Encoding.ASCII.GetBytes(name));
            encoded.Add((byte)value.Length);
            encoded.AddRange(Encoding.ASCII.GetBytes(value));
            return encoded.ToArray();
        }

        static async Task WriteFrame(NetworkStream wire, byte type, byte flags, int streamId, byte[] payload,
            CancellationToken cancellation)
        {
            var frame = new byte[payload.Length + 9];
            frame[0] = (byte)(payload.Length >> 16);
            frame[1] = (byte)(payload.Length >> 8);
            frame[2] = (byte)payload.Length;
            frame[3] = type;
            frame[4] = flags;
            frame[5] = (byte)(streamId >> 24);
            frame[6] = (byte)(streamId >> 16);
            frame[7] = (byte)(streamId >> 8);
            frame[8] = (byte)streamId;
            Buffer.BlockCopy(payload, 0, frame, 9, payload.Length);
            await WithinTimeout(wire.WriteAsync(frame, 0, frame.Length, cancellation));
        }

        static async Task<TcpClient> AcceptPeerOrReportCallFailure(TcpListener listener, Task<bool> response)
        {
            var accepted = listener.AcceptTcpClientAsync();
            if (await WithinTimeout(Task.WhenAny(accepted, response)) == response)
            {
                // Surface missing native libraries or runtime initialization failures immediately,
                // instead of hiding them behind a generic TCP accept timeout.
                await response;
                Assert.Fail("The RPC ended before establishing its HTTP/2 connection.");
            }
            return await accepted;
        }

        static async Task<byte[]> ReadRequestBody(NetworkStream wire, CancellationToken cancellation)
        {
            var body = new List<byte>();
            for (var frame = 0; frame < 32; frame++)
            {
                var header = await ReadExactly(wire, 9, cancellation);
                var length = (header[0] << 16) | (header[1] << 8) | header[2];
                Assert.That(length, Is.LessThanOrEqualTo(65536), "Unexpectedly large probe frame.");
                var payload = await ReadExactly(wire, length, cancellation);
                if (header[3] == 4 && (header[4] & 1) == 0) // Acknowledge client SETTINGS.
                    await WriteFrame(wire, 4, 1, 0, Array.Empty<byte>(), cancellation);
                if (header[3] != 0) // DATA
                    continue;
                Assert.That(header[5] | header[6] | header[7], Is.Zero);
                Assert.That(header[8], Is.EqualTo(1), "The fresh channel's first request must use stream 1.");
                Assert.That(header[4] & 8, Is.Zero, "The probe does not expect padded DATA frames.");
                body.AddRange(payload);
                if (body.Count >= Request.Length + 5)
                    return body.ToArray();
            }
            Assert.Fail("No gRPC request body arrived over HTTP/2.");
            return null;
        }

        static async Task<byte[]> ReadExactly(NetworkStream stream, int length, CancellationToken cancellation)
        {
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer, offset, length - offset, cancellation);
                if (read == 0)
                    throw new EndOfStreamException("The HTTP/2 peer closed unexpectedly.");
                offset += read;
            }
            return buffer;
        }

        static async Task WithinTimeout(Task operation)
        {
            if (await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(8))) != operation)
                Assert.Fail("The native transport operation did not complete within 8 seconds.");
            await operation;
        }

        static async Task<T> WithinTimeout<T>(Task<T> operation)
        {
            await WithinTimeout((Task)operation);
            return await operation;
        }
    }
}
