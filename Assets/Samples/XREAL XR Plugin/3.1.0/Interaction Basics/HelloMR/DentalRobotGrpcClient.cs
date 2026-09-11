using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dentalmodeltransfer;
using Google.Protobuf;
using Grpc.Net.Client;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public class DentalRobotGrpcClient : MonoBehaviour
    {
        static readonly TimeSpan MessageInactivityTimeout = TimeSpan.FromSeconds(30);

        [SerializeField]
        bool m_ConnectOnStart = true;

        [SerializeField]
        bool m_AutoSearchInBackground = true;

        [SerializeField]
        float m_SearchIntervalSeconds = 5f;

        [SerializeField]
        string m_ServerHost = DentalRobotConnectionDefaults.ServerHost;

        [SerializeField]
        int m_ServerPort = DentalRobotConnectionDefaults.ServerPort;

        [SerializeField]
        string m_DeviceId = DentalRobotConnectionDefaults.DeviceId;

        [SerializeField]
        string m_DatasetId = DentalRobotConnectionDefaults.DatasetId;

        [SerializeField]
        bool m_SendAckMessages = true;

        readonly ConcurrentQueue<Action> m_MainThreadActions = new ConcurrentQueue<Action>();

        CancellationTokenSource m_Cancellation;
        Task m_StreamTask;
        float m_NextSearchRealtime;
        bool m_ManualSearchPending;

        string ServerAddress => $"http://{m_ServerHost}:{m_ServerPort}";

        public void ConfigureEndpoint(string serverHost, int serverPort, string deviceId, string datasetId)
        {
            if (!string.IsNullOrEmpty(serverHost))
                m_ServerHost = serverHost;

            if (serverPort > 0)
                m_ServerPort = serverPort;

            if (!string.IsNullOrEmpty(deviceId))
                m_DeviceId = deviceId;

            if (!string.IsNullOrEmpty(datasetId))
                m_DatasetId = datasetId;
        }

        void Start()
        {
            if (m_ConnectOnStart)
                Connect();
            else
                ScheduleNextSearch();
        }

        void Update()
        {
            while (m_MainThreadActions.TryDequeue(out var action))
                action?.Invoke();

            if (m_ManualSearchPending && !IsStreamRunning())
            {
                m_ManualSearchPending = false;
                Connect();
                return;
            }

            if (!m_ConnectOnStart || !m_AutoSearchInBackground)
                return;

            if (IsStreamRunning())
                return;

            if (Time.realtimeSinceStartup >= m_NextSearchRealtime)
                Connect();
        }

        void OnDestroy()
        {
            Disconnect();
        }

        public void Connect()
        {
            if (IsStreamRunning())
                return;

            CleanupCompletedCancellation();
            var cancellation = new CancellationTokenSource();
            m_Cancellation = cancellation;
            ScheduleNextSearch();
            var serverAddress = ServerAddress;
            var deviceId = m_DeviceId;
            var datasetId = m_DatasetId;
            var cancellationToken = cancellation.Token;
            EnqueueLinkConnecting($"正在搜索/连接手术机器人 gRPC: {serverAddress}");
            m_StreamTask = Task.Run(() => RunStreamAsync(serverAddress, deviceId, datasetId, cancellationToken, cancellation));
        }

        /// <summary>
        /// Stops the current stream, applies a new endpoint and searches it immediately.
        /// If cancellation is still completing, Update starts the new search as soon as it is safe.
        /// </summary>
        public void SearchEndpoint(string serverHost, int serverPort)
        {
            ConfigureEndpoint(serverHost, serverPort, m_DeviceId, m_DatasetId);
            m_ManualSearchPending = true;

            if (IsStreamRunning())
            {
                EnqueueLinkConnecting($"正在切换到 gRPC 服务端: {ServerAddress}");
                Disconnect();
                return;
            }

            m_ManualSearchPending = false;
            Connect();
        }

        public void Disconnect()
        {
            var cancellation = m_Cancellation;
            m_Cancellation = null;
            if (cancellation != null)
                cancellation.Cancel();
        }

        async Task RunStreamAsync(string serverAddress, string deviceId, string datasetId, CancellationToken cancellationToken, CancellationTokenSource cancellation)
        {
            try
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                using (var channel = GrpcChannel.ForAddress(serverAddress))
                {
                    var client = new DentalModelTransfer.DentalModelTransferClient(channel.CreateCallInvoker());
                    using (var call = client.StreamDentalModel(cancellationToken: cancellationToken))
                    {
                        await call.RequestStream.WriteAsync(new ClientMessage
                        {
                            Request = new ModelRequest
                            {
                                DeviceId = deviceId,
                                DatasetId = datasetId,
                            }
                        }).ConfigureAwait(false);

                        EnqueueStatus($"已发送模型请求 device_id={deviceId}, dataset_id={datasetId}");

                        while (true)
                        {
                            // The server pushes data unprompted; a silent endpoint would otherwise
                            // park this task forever and block the auto-reconnect loop.
                            var moveNext = call.ResponseStream.MoveNext(cancellationToken);
                            var completed = await Task.WhenAny(
                                moveNext,
                                Task.Delay(MessageInactivityTimeout, cancellationToken)).ConfigureAwait(false);
                            if (completed != moveNext)
                            {
                                EnqueueLinkLost($"{MessageInactivityTimeout.TotalSeconds:0} 秒未收到手术机器人数据，断开并稍后重试。");
                                break;
                            }

                            if (!await moveNext.ConfigureAwait(false))
                                break;

                            var message = call.ResponseStream.Current;
                            await HandleServerMessageAsync(call, message, cancellationToken).ConfigureAwait(false);

                            if (message.PayloadCase == ServerMessage.PayloadOneofCase.End)
                                break;
                        }

                        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                EnqueueLinkLost("已断开手术机器人 gRPC。");
            }
            catch (Exception ex)
            {
                EnqueueLinkLost($"手术机器人 gRPC 连接失败: {ex.GetType().Name}: {ex.Message}");
                EnqueueException(ex);
            }
            finally
            {
                cancellation.Dispose();
                m_MainThreadActions.Enqueue(ScheduleNextSearch);
            }
        }

        bool IsStreamRunning()
        {
            return m_StreamTask != null && !m_StreamTask.IsCompleted;
        }

        void ScheduleNextSearch()
        {
            m_NextSearchRealtime = Time.realtimeSinceStartup + Mathf.Max(1f, m_SearchIntervalSeconds);
        }

        void CleanupCompletedCancellation()
        {
            if (m_StreamTask != null && !m_StreamTask.IsCompleted)
                return;

            if (m_Cancellation == null)
                return;

            m_Cancellation.Dispose();
            m_Cancellation = null;
        }

        async Task HandleServerMessageAsync(Grpc.Core.AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call, ServerMessage message, CancellationToken cancellationToken)
        {
            switch (message.PayloadCase)
            {
                case ServerMessage.PayloadOneofCase.Metadata:
                    HandleMetadata(message.Metadata);
                    if (m_SendAckMessages)
                        await SendAckAsync(call, message.Metadata.DatasetId, "metadata received", cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.StlChunk:
                    HandleStlChunk(message.StlChunk);
                    break;

                case ServerMessage.PayloadOneofCase.End:
                    HandleTransferEnd(message.End);
                    if (m_SendAckMessages)
                        await SendAckAsync(call, message.End.DatasetId, "transfer end received", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        async Task SendAckAsync(Grpc.Core.AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call, string datasetId, string message, CancellationToken cancellationToken)
        {
            await call.RequestStream.WriteAsync(new ClientMessage
            {
                Ack = new ClientAck
                {
                    DatasetId = datasetId ?? string.Empty,
                    Message = message ?? string.Empty,
                }
            }).ConfigureAwait(false);
        }

        void HandleMetadata(ModelMetadata metadata)
        {
            var matrix = metadata.DrillFromTeeth;
            m_MainThreadActions.Enqueue(() =>
            {
                DentalNavigationState.EnsureInstance().ApplyMetadata(
                    metadata.DatasetId,
                    matrix,
                    metadata.Distance,
                    metadata.LateralDistance,
                    metadata.Angle);
            });
        }

        void HandleStlChunk(StlChunk chunk)
        {
            var modelType = chunk.ModelType.ToString();
            var data = chunk.Data == null ? Array.Empty<byte>() : chunk.Data.ToByteArray();
            m_MainThreadActions.Enqueue(() =>
            {
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.ApplyStlChunk(chunk.DatasetId, modelType, chunk.Filename, chunk.Offset, data.Length, data);
            });
        }

        void HandleTransferEnd(TransferEnd end)
        {
            m_MainThreadActions.Enqueue(() =>
            {
                DentalNavigationState.EnsureInstance().NotifyModelTransfer(end.Ok, end.Message);
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.ApplyTransferEnd(end.DatasetId, end.Ok, end.Message, end.TeethBytes, end.DrillBytes);
            });
        }

        void EnqueueStatus(string status)
        {
            m_MainThreadActions.Enqueue(() =>
            {
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.SetConnectionStatus(status);

                Debug.Log($"DentalRobotGrpcClient: {status}");
            });
        }

        void EnqueueLinkConnecting(string status)
        {
            m_MainThreadActions.Enqueue(() =>
            {
                DentalNavigationState.EnsureInstance().NotifyConnecting(status);
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.SetConnectionStatus(status);

                Debug.Log($"DentalRobotGrpcClient: {status}");
            });
        }

        void EnqueueLinkLost(string status)
        {
            m_MainThreadActions.Enqueue(() =>
            {
                DentalNavigationState.EnsureInstance().NotifyDisconnected(status);
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.SetConnectionStatus(status);

                Debug.Log($"DentalRobotGrpcClient: {status}");
            });
        }

        void EnqueueException(Exception exception)
        {
            m_MainThreadActions.Enqueue(() => Debug.LogException(exception));
        }
    }
}
