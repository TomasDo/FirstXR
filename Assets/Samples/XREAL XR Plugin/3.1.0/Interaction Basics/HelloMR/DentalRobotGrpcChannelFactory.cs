using System;
using Cysharp.Net.Http;
using Grpc.Net.Client;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Creates native HTTP/2 channels for the Android IL2CPP player and the Editor.
    /// </summary>
    public static class DentalRobotGrpcChannelFactory
    {
        public static GrpcChannel Create(string serverAddress)
        {
            var handler = new YetAnotherHttpHandler
            {
                // The navigation server uses cleartext HTTP/2 (h2c), including bidirectional streams.
                Http2Only = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
            };

            try
            {
                return GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
                {
                    HttpHandler = handler,
                    // Each connection attempt owns its native handler, including on cancellation/reconnect.
                    DisposeHttpClient = true,
                });
            }
            catch
            {
                handler.Dispose();
                throw;
            }
        }
    }
}
