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
        [SerializeField]
        bool m_ConnectOnStart = true;

        [SerializeField]
        bool m_AutoSearchInBackground = true;

        [SerializeField]
        float m_SearchIntervalSeconds = 5f;

        [SerializeField]
        string m_ServerHost = "192.168.31.166";

        [SerializeField]
        int m_ServerPort = 50051;

        [SerializeField]
        string m_DeviceId = "beam-pro";

        [SerializeField]
        string m_DatasetId = "default";

        [SerializeField]
        bool m_SendAckMessages = true;

        readonly ConcurrentQueue<Action> m_MainThreadActions = new ConcurrentQueue<Action>();

        CancellationTokenSource m_Cancellation;
        Task m_StreamTask;
        float m_NextSearchRealtime;

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
            m_Cancellation = new CancellationTokenSource();
            ScheduleNextSearch();
            EnqueueStatus($"正在搜索/连接手术机器人 gRPC: {ServerAddress}");
            m_StreamTask = Task.Run(() => RunStreamAsync(m_Cancellation.Token));
        }

        public void Disconnect()
        {
            if (m_Cancellation == null)
                return;

            m_Cancellation.Cancel();
            m_Cancellation.Dispose();
            m_Cancellation = null;
        }

        async Task RunStreamAsync(CancellationToken cancellationToken)
        {
            try
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                using (var channel = GrpcChannel.ForAddress(ServerAddress))
                {
                    var client = new DentalModelTransfer.DentalModelTransferClient(channel);
                    using (var call = client.StreamDentalModel(cancellationToken: cancellationToken))
                    {
                        await call.RequestStream.WriteAsync(new ClientMessage
                        {
                            Request = new ModelRequest
                            {
                                DeviceId = m_DeviceId,
                                DatasetId = m_DatasetId,
                            }
                        }).ConfigureAwait(false);

                        EnqueueStatus($"已发送模型请求 device_id={m_DeviceId}, dataset_id={m_DatasetId}");

                        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
                        {
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
                EnqueueStatus("已断开手术机器人 gRPC。");
            }
            catch (Exception ex)
            {
                EnqueueStatus($"手术机器人 gRPC 连接失败: {ex.GetType().Name}: {ex.Message}");
                EnqueueException(ex);
            }
            finally
            {
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
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.ApplyMetadata(metadata.DatasetId, matrix, metadata.Distance, metadata.LateralDistance, metadata.Angle);
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

        void EnqueueException(Exception exception)
        {
            m_MainThreadActions.Enqueue(() => Debug.LogException(exception));
        }
    }
}
