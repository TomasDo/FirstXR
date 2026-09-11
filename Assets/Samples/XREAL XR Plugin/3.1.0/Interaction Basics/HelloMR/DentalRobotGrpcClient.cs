using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dentalmodeltransfer;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public class DentalRobotGrpcClient : MonoBehaviour
    {
        const uint ProtocolVersion = 2;
        const int MainThreadQueueCapacity = 256;
        const int OutgoingQueueCapacity = 64;
        const int MaxAssetCount = 8192;
        const int MaxAssetChunkBytes = 4 * 1024 * 1024;
        const long MaxSingleDicomBytes = 512L * 1024L * 1024L;
        const long MaxTotalAssetBytes = 16L * 1024L * 1024L * 1024L;
        const string ExplicitVrLittleEndianUid = "1.2.840.10008.1.2.1";

        static readonly TimeSpan MessageInactivityTimeout = TimeSpan.FromSeconds(30);

        [SerializeField]
        bool m_ConnectOnStart = true;

        [SerializeField]
        bool m_AutoSearchInBackground = true;

        [SerializeField]
        float m_SearchIntervalSeconds = 5f;

        [SerializeField]
        int m_MaxMainThreadActionsPerFrame = 64;

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

        readonly BoundedDispatchQueue m_MainThreadActions = new BoundedDispatchQueue(MainThreadQueueCapacity);
        readonly object m_LatestFrameLock = new object();
        readonly object m_LatestLegacyMetadataLock = new object();
        readonly object m_AssetDescriptorLock = new object();
        readonly object m_PendingAssetContextLock = new object();
        readonly ConcurrentDictionary<string, AssetReceiveProgress> m_AssetProgress = new ConcurrentDictionary<string, AssetReceiveProgress>();
        readonly Dictionary<string, AssetDescriptor> m_AssetDescriptors = new Dictionary<string, AssetDescriptor>();
        readonly HashSet<string> m_CompletedDicomAssets = new HashSet<string>(StringComparer.Ordinal);

        CancellationTokenSource m_Cancellation;
        Task m_StreamTask;
        bool m_Started;
        float m_NextSearchRealtime;
        bool m_ManualSearchPending;
        volatile bool m_ReconnectAllowed = true;
        volatile bool m_ServerSessionEnded;
        volatile bool m_AssetSessionEnded;
        volatile bool m_AssetStreamEndReceived;
        volatile bool m_AssetTransferInProgress;
        volatile bool m_HasV2Context;
        volatile string m_ServerSessionId = string.Empty;
        string m_ClientSessionId = string.Empty;

        AsyncRequestWriter<ClientMessage> m_SessionWriter;
        AsyncRequestWriter<AssetClientMessage> m_AssetWriter;
        long m_CommandSequence;

        NavigationFrame m_LatestFrame;
        bool m_HasLatestFrame;
        string m_LatestFrameSessionId = string.Empty;
        ulong m_LatestFrameContextVersion;
        ulong m_LatestFrameSequence;
        bool m_HasLatestFrameSequence;

        ModelMetadata m_LatestLegacyMetadata;
        bool m_HasLatestLegacyMetadata;

        DentalNavigationContext m_PendingAssetContext;
        bool m_HasPendingAssetContext;
        bool m_HasAssetContext;
        string m_ActiveTransferId = string.Empty;
        string m_ActiveTransferSessionId = string.Empty;
        string m_ActiveTransferDatasetId = string.Empty;
        ulong m_ActiveTransferContextVersion;
        long m_ActiveTransferGeneration;
        long m_ActiveDicomTransferGeneration;
        string m_ActiveDicomTransferNamespace = string.Empty;
        string m_CommittedTransferId = string.Empty;
        DentalCtVolumeService m_CtVolumeService;
        XrRgbRtpStreamer m_ObservationStreamer;
        bool m_ObservationStatusPending;
        float m_NextObservationStatusRealtime;
        string m_LastObservationStatusSignature = string.Empty;

        string ServerAddress => $"http://{m_ServerHost}:{m_ServerPort}";

        // These asset events run on Unity's main thread. Consumers must copy any
        // payload they retain and return quickly so the gRPC stream can apply
        // backpressure instead of growing an unbounded queue.
        public event Action<AssetManifest> AssetManifestReceived;
        public event Action<AssetChunk> AssetChunkReceived;
        public event Action<AssetTransferComplete> AssetTransferCompleted;
        public event Action<CommandResult> CommandResultReceived;

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
            m_Started = true;
            BindObservationStreamer();
            if (m_ConnectOnStart)
                Connect();
            else
                ScheduleNextSearch();
        }

        void OnEnable()
        {
            RgbSliceGestureController.AnySliceStepRequested += OnLocalSliceStepRequested;
            BindObservationStreamer();
            m_ObservationStatusPending = true;
            if (m_Started && m_ConnectOnStart)
            {
                m_ReconnectAllowed = true;
                m_ManualSearchPending = true;
                if (!IsStreamRunning())
                {
                    m_ManualSearchPending = false;
                    Connect();
                }
            }
        }

        void OnDisable()
        {
            RgbSliceGestureController.AnySliceStepRequested -= OnLocalSliceStepRequested;
            UnbindObservationStreamer();
            m_ManualSearchPending = false;
            Disconnect();
        }

        void Update()
        {
            var actionBudget = Mathf.Max(1, m_MaxMainThreadActionsPerFrame);
            for (var i = 0; i < actionBudget && m_MainThreadActions.TryDequeue(out var work); i++)
                work?.Invoke();

            ApplyLatestNavigationFrame();
            ApplyLatestLegacyMetadata();
            BindObservationStreamer();
            PumpObservationStatus();

            if (m_ManualSearchPending && !IsStreamRunning())
            {
                m_ManualSearchPending = false;
                Connect();
                return;
            }

            if (!m_ConnectOnStart || !m_AutoSearchInBackground || !m_ReconnectAllowed)
                return;

            if (IsStreamRunning())
                return;

            if (Time.realtimeSinceStartup >= m_NextSearchRealtime)
                Connect();
        }

        void OnDestroy()
        {
            UnbindObservationStreamer();
            Disconnect();
            m_MainThreadActions.Dispose();
        }

        public void Connect()
        {
            if (IsStreamRunning())
                return;

            CleanupCompletedCancellation();
            m_ReconnectAllowed = true;
            m_ServerSessionEnded = false;
            m_AssetSessionEnded = false;
            m_AssetStreamEndReceived = false;
            m_AssetTransferInProgress = false;
            m_HasV2Context = false;
            m_ClientSessionId = Guid.NewGuid().ToString("N");
            m_LastObservationStatusSignature = string.Empty;
            m_ObservationStatusPending = true;
            ResetLatestFrames();
            lock (m_PendingAssetContextLock)
            {
                m_HasPendingAssetContext = false;
                m_HasAssetContext = false;
            }

            var cancellation = new CancellationTokenSource();
            m_Cancellation = cancellation;
            ScheduleNextSearch();
            var serverAddress = ServerAddress;
            var deviceId = m_DeviceId;
            var datasetId = m_DatasetId;
            var cancellationToken = cancellation.Token;
            EnqueueLinkConnecting($"正在搜索/连接手术机器人 gRPC: {serverAddress}");
            m_StreamTask = Task.Run(() => RunConnectionAsync(
                serverAddress,
                deviceId,
                datasetId,
                m_ClientSessionId,
                cancellationToken,
                cancellation));
        }

        /// <summary>
        /// Stops the current stream, applies a new endpoint and searches it immediately.
        /// If cancellation is still completing, Update starts the new search as soon as it is safe.
        /// </summary>
        public void SearchEndpoint(string serverHost, int serverPort)
        {
            ConfigureEndpoint(serverHost, serverPort, m_DeviceId, m_DatasetId);
            m_ManualSearchPending = true;
            m_ReconnectAllowed = true;

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

        public bool QueueSliceDelta(int deltaSteps, ulong expectedControlVersion = 0)
        {
            if (deltaSteps == 0)
                return false;

            var state = DentalNavigationState.Instance;
            var snapshot = state != null ? state.Capture(Time.realtimeSinceStartup) : default(DentalNavigationSnapshot);
            if (!snapshot.HasContext)
                return false;

            if (expectedControlVersion == 0 && snapshot.HasSliceState)
                expectedControlVersion = snapshot.SliceState.ControlVersion;

            return TryQueueSessionMessage(new ClientMessage
            {
                SliceCommand = new SliceCommand
                {
                    SessionId = snapshot.SessionId,
                    ContextVersion = snapshot.ContextVersion,
                    BaseControlVersion = expectedControlVersion,
                    CommandSequence = NextCommandSequence(),
                    Source = ControlSource.XrealGesture,
                    DeltaSteps = deltaSteps,
                }
            });
        }

        public bool QueueSliceSelection(double offsetMm, ulong expectedControlVersion = 0)
        {
            if (double.IsNaN(offsetMm) || double.IsInfinity(offsetMm))
                return false;

            var state = DentalNavigationState.Instance;
            var snapshot = state != null ? state.Capture(Time.realtimeSinceStartup) : default(DentalNavigationSnapshot);
            if (!snapshot.HasContext)
                return false;

            if (expectedControlVersion == 0 && snapshot.HasSliceState)
                expectedControlVersion = snapshot.SliceState.ControlVersion;

            return TryQueueSessionMessage(new ClientMessage
            {
                SliceCommand = new SliceCommand
                {
                    SessionId = snapshot.SessionId,
                    ContextVersion = snapshot.ContextVersion,
                    BaseControlVersion = expectedControlVersion,
                    CommandSequence = NextCommandSequence(),
                    Source = ControlSource.XrealGesture,
                    OffsetMm = offsetMm,
                }
            });
        }

        public bool QueueDisplayState(DentalDisplayLayoutState layout, bool accepted = true, string message = "")
        {
            return TryQueueSessionMessage(new ClientMessage
            {
                DisplayState = new DisplayStateReport
                {
                    Accepted = accepted,
                    Message = message ?? string.Empty,
                    AppliedLayout = ToProto(layout),
                }
            });
        }

        public bool QueueObservationStatus(bool mirrorAvailable, bool rgbAvailable, string error)
        {
            var state = DentalNavigationState.Instance;
            var snapshot = state != null ? state.Capture(Time.realtimeSinceStartup) : default(DentalNavigationSnapshot);
            var controlVersion = snapshot.HasObservationControl ? snapshot.ObservationControl.ControlVersion : 0;
            return QueueObservationStatus(
                snapshot.SessionId,
                snapshot.ContextVersion,
                controlVersion,
                mirrorAvailable,
                rgbAvailable,
                error);
        }

        bool QueueObservationStatus(
            string sessionId,
            ulong contextVersion,
            ulong controlVersion,
            bool mirrorAvailable,
            bool rgbAvailable,
            string error)
        {
            return TryQueueSessionMessage(new ClientMessage
            {
                ObservationStatus = new ObservationStatus
                {
                    SessionId = sessionId ?? string.Empty,
                    ContextVersion = contextVersion,
                    ControlVersion = controlVersion,
                    XrMirrorAvailable = mirrorAvailable,
                    RgbAvailable = rgbAvailable,
                    Error = error ?? string.Empty,
                }
            });
        }

        void OnLocalSliceStepRequested(RgbSliceStepEventArgs args)
        {
            if (args != null)
                QueueSliceDelta(args.Step, args.ControlVersion);
        }

        async Task RunConnectionAsync(
            string serverAddress,
            string deviceId,
            string datasetId,
            string clientSessionId,
            CancellationToken cancellationToken,
            CancellationTokenSource cancellation)
        {
            try
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                using (var channel = GrpcChannel.ForAddress(serverAddress))
                {
                    var client = new DentalModelTransfer.DentalModelTransferClient(channel.CreateCallInvoker());
                    try
                    {
                        await RunV2ConnectionAsync(client, deviceId, datasetId, clientSessionId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented && !cancellationToken.IsCancellationRequested)
                    {
                        EnqueueStatus("服务端不支持协议 v2，已切换到 v1 兼容模式。");
                        m_HasV2Context = false;
                        ResetLatestFrames();
                        await RunSessionCallAsync(
                            client.StreamDentalModel(cancellationToken: cancellationToken),
                            deviceId,
                            datasetId,
                            clientSessionId,
                            false,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!cancellationToken.IsCancellationRequested && !m_ServerSessionEnded)
                    EnqueueLinkLost("手术机器人 gRPC 数据流已结束，稍后重试。");
            }
            catch (OperationCanceledException)
            {
                if (!m_ServerSessionEnded)
                    EnqueueLinkLost("已断开手术机器人 gRPC。");
            }
            catch (Exception ex)
            {
                EnqueueLinkLost($"手术机器人 gRPC 连接失败: {ex.GetType().Name}: {ex.Message}");
                EnqueueException(ex);
            }
            finally
            {
                Interlocked.CompareExchange(ref m_Cancellation, null, cancellation);
                cancellation.Dispose();
                TryEnqueueMainThread(ScheduleNextSearch);
            }
        }

        async Task RunV2ConnectionAsync(
            DentalModelTransfer.DentalModelTransferClient client,
            string deviceId,
            string datasetId,
            string clientSessionId,
            CancellationToken cancellationToken)
        {
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var assetTask = RunAssetStreamProtectedAsync(
                    client,
                    deviceId,
                    datasetId,
                    linkedCancellation.Token);
                try
                {
                    await RunSessionCallAsync(
                        client.StreamSession(cancellationToken: cancellationToken),
                        deviceId,
                        datasetId,
                        clientSessionId,
                        true,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    linkedCancellation.Cancel();
                    try
                    {
                        await assetTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }

        async Task RunAssetStreamProtectedAsync(
            DentalModelTransfer.DentalModelTransferClient client,
            string deviceId,
            string datasetId,
            CancellationToken cancellationToken)
        {
            var retryDelaySeconds = 1;
            while (!cancellationToken.IsCancellationRequested && !m_AssetSessionEnded)
            {
                try
                {
                    RequeueLastAssetRequest();
                    await RunAssetStreamAsync(client, deviceId, datasetId, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested || m_AssetSessionEnded)
                        return;
                    EnqueueStatus("资产通道已结束，正在保留进度并重连。");
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
                {
                    EnqueueStatus("服务端未提供 v2 资产通道；导航通道继续运行。");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    EnqueueStatus($"资产通道不可用，导航继续运行，将断点重连: {ex.Message}");
                    EnqueueException(ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken).ConfigureAwait(false);
                retryDelaySeconds = Math.Min(5, retryDelaySeconds * 2);
            }
        }

        async Task RunSessionCallAsync(
            AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call,
            string deviceId,
            string datasetId,
            string clientSessionId,
            bool isV2,
            CancellationToken cancellationToken)
        {
            using (call)
            {
                var writer = new AsyncRequestWriter<ClientMessage>(
                    call.RequestStream,
                    OutgoingQueueCapacity,
                    cancellationToken);
                Interlocked.Exchange(ref m_SessionWriter, writer);
                try
                {
                    var request = new ModelRequest
                    {
                        DeviceId = deviceId ?? string.Empty,
                        DatasetId = datasetId ?? string.Empty,
                        ProtocolVersion = isV2 ? ProtocolVersion : 1,
                        ClientSessionId = clientSessionId ?? string.Empty,
                    };
                    if (isV2)
                    {
                        request.RequestedCapabilities.Add(Capability.NavigationV2);
                        request.RequestedCapabilities.Add(Capability.DicomAssets);
                        request.RequestedCapabilities.Add(Capability.SliceControl);
                        request.RequestedCapabilities.Add(Capability.DisplayLayout);
                        request.RequestedCapabilities.Add(Capability.XrMirror);
                        request.RequestedCapabilities.Add(Capability.RgbView);
                    }

                    await writer.EnqueueAsync(new ClientMessage { Request = request }, cancellationToken).ConfigureAwait(false);
                    EnqueueStatus($"已发送{(isV2 ? "导航会话" : "模型")}请求 device_id={deviceId}, dataset_id={datasetId}");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var moveNext = call.ResponseStream.MoveNext(cancellationToken);
                        var completed = await Task.WhenAny(
                            moveNext,
                            Task.Delay(MessageInactivityTimeout, cancellationToken)).ConfigureAwait(false);
                        if (completed != moveNext)
                            throw new TimeoutException($"{MessageInactivityTimeout.TotalSeconds:0} 秒未收到手术机器人数据。");

                        if (!await moveNext.ConfigureAwait(false))
                            break;

                        var message = call.ResponseStream.Current;
                        await HandleServerMessageAsync(writer, message, cancellationToken).ConfigureAwait(false);

                        if (message.PayloadCase == ServerMessage.PayloadOneofCase.SessionEnd
                            && m_ServerSessionEnded)
                            break;
                    }
                }
                finally
                {
                    Interlocked.CompareExchange(ref m_SessionWriter, null, writer);
                    await writer.StopAsync().ConfigureAwait(false);
                    try
                    {
                        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    writer.Dispose();
                }
            }
        }

        async Task RunAssetStreamAsync(
            DentalModelTransfer.DentalModelTransferClient client,
            string deviceId,
            string datasetId,
            CancellationToken cancellationToken)
        {
            m_AssetStreamEndReceived = false;
            m_AssetTransferInProgress = false;
            using (var call = client.StreamAssets(cancellationToken: cancellationToken))
            {
                var writer = new AsyncRequestWriter<AssetClientMessage>(
                    call.RequestStream,
                    OutgoingQueueCapacity,
                    cancellationToken);
                Interlocked.Exchange(ref m_AssetWriter, writer);
                try
                {
                    TrySendPendingAssetRequest(writer);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var moveNext = call.ResponseStream.MoveNext(cancellationToken);
                        if (m_AssetTransferInProgress)
                        {
                            var completed = await Task.WhenAny(
                                moveNext,
                                Task.Delay(MessageInactivityTimeout, cancellationToken)).ConfigureAwait(false);
                            if (completed != moveNext)
                                throw new TimeoutException($"资产传输进行中 {MessageInactivityTimeout.TotalSeconds:0} 秒未收到数据。");
                        }

                        if (!await moveNext.ConfigureAwait(false))
                            break;

                        var message = call.ResponseStream.Current;
                        await HandleAssetServerMessageAsync(writer, message, cancellationToken).ConfigureAwait(false);
                        if (message.PayloadCase == AssetServerMessage.PayloadOneofCase.SessionEnd
                            && m_AssetStreamEndReceived)
                            break;
                    }
                }
                finally
                {
                    Interlocked.CompareExchange(ref m_AssetWriter, null, writer);
                    await writer.StopAsync().ConfigureAwait(false);
                    try
                    {
                        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    writer.Dispose();
                }
            }
        }

        async Task HandleServerMessageAsync(
            AsyncRequestWriter<ClientMessage> writer,
            ServerMessage message,
            CancellationToken cancellationToken)
        {
            switch (message.PayloadCase)
            {
                case ServerMessage.PayloadOneofCase.Metadata:
                    StoreLatestLegacyMetadata(message.Metadata);
                    await SendAckAsync(
                        writer,
                        message.Metadata.DatasetId,
                        AckKind.LegacyMessage,
                        "metadata",
                        0,
                        true,
                        "metadata received",
                        cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.StlChunk:
                    await HandleLegacyStlChunkAsync(message.StlChunk, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.End:
                    await HandleLegacyTransferEndAsync(message.End, cancellationToken).ConfigureAwait(false);
                    await SendAckAsync(
                        writer,
                        message.End.DatasetId,
                        AckKind.LegacyMessage,
                        "transfer_end",
                        0,
                        message.End.Ok,
                        "transfer end received; session remains open",
                        cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.NavigationContext:
                    await HandleNavigationContextAsync(writer, message.NavigationContext, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.NavigationFrame:
                    StoreLatestNavigationFrame(message.NavigationFrame);
                    break;

                case ServerMessage.PayloadOneofCase.ToleranceConfig:
                    await HandleToleranceConfigAsync(writer, message.ToleranceConfig, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.SliceState:
                    await HandleSliceStateAsync(writer, message.SliceState, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.DisplayLayout:
                    await HandleDisplayLayoutAsync(writer, message.DisplayLayout, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.ObservationControl:
                    await HandleObservationControlAsync(writer, message.ObservationControl, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.NavigationStatus:
                    await HandleNavigationStatusAsync(message.NavigationStatus, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.SessionEnd:
                    await HandleSessionEndAsync(writer, message.SessionEnd, cancellationToken).ConfigureAwait(false);
                    break;

                case ServerMessage.PayloadOneofCase.Heartbeat:
                    writer.TryEnqueue(new ClientMessage
                    {
                        Heartbeat = new Heartbeat
                        {
                            SessionId = message.Heartbeat.SessionId,
                            SentTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        }
                    });
                    break;

                case ServerMessage.PayloadOneofCase.CommandResult:
                    var result = message.CommandResult.Clone();
                    await EnqueueMainThreadAsync(() =>
                    {
                        var state = DentalNavigationState.Instance;
                        var snapshot = state != null
                            ? state.Capture(Time.realtimeSinceStartup)
                            : default(DentalNavigationSnapshot);
                        if (snapshot.HasContext
                            && snapshot.ContextVersion == result.ContextVersion
                            && string.Equals(snapshot.SessionId, result.SessionId, StringComparison.Ordinal))
                            CommandResultReceived?.Invoke(result);
                    }, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        async Task HandleNavigationContextAsync(
            AsyncRequestWriter<ClientMessage> writer,
            NavigationContext message,
            CancellationToken cancellationToken)
        {
            var unitsValid = message.DistanceUnit == DistanceUnit.Millimeter && message.AngleUnit == AngleUnit.Degree;
            var context = ToDomain(message);
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                if (!unitsValid)
                    return false;

                var state = DentalNavigationState.EnsureInstance();
                var before = state.Capture(Time.realtimeSinceStartup);
                var alreadyCurrent = before.HasContext &&
                    before.ContextVersion == context.ContextVersion &&
                    string.Equals(before.SessionId, context.SessionId, StringComparison.Ordinal);
                var applied = state.ApplyNavigationContext(context);
                if (applied)
                {
                    QueueAssetRequestForContext(context, !alreadyCurrent);
                    if (!alreadyCurrent && RgbSliceGestureController.Instance != null)
                        RgbSliceGestureController.Instance.ResetForContext();
                }
                return applied;
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                m_HasV2Context = true;
                m_ServerSessionId = message.SessionId ?? string.Empty;
            }

            await SendAckAsync(
                writer,
                message.DatasetId,
                AckKind.Context,
                message.PlanId,
                message.ContextVersion,
                accepted,
                accepted ? "context applied" : "context rejected: stale, duplicate, invalid, or unsupported units",
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleToleranceConfigAsync(
            AsyncRequestWriter<ClientMessage> writer,
            ToleranceConfig message,
            CancellationToken cancellationToken)
        {
            var unitsValid = message.DistanceUnit == DistanceUnit.Millimeter && message.AngleUnit == AngleUnit.Degree;
            var thresholds = ToDomain(message);
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.EnsureInstance();
                if (!unitsValid)
                {
                    state.NotifyThresholdsUnavailable(
                        message.SessionId,
                        message.ContextVersion,
                        message.ConfigVersion,
                        "阈值单位必须为 mm 和 °");
                    return false;
                }

                return state.ApplyThresholds(thresholds);
            }, cancellationToken).ConfigureAwait(false);

            await SendAckAsync(
                writer,
                string.Empty,
                AckKind.Tolerance,
                "tolerance",
                message.ConfigVersion,
                accepted,
                accepted ? "tolerance applied" : "tolerance rejected",
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleSliceStateAsync(
            AsyncRequestWriter<ClientMessage> writer,
            SliceState message,
            CancellationToken cancellationToken)
        {
            var sliceState = ToDomain(message);
            var executionDisposition = DentalSliceExecutionDisposition.None;
            var executionError = string.Empty;
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.EnsureInstance();
                if (!state.CanApplySliceState(sliceState))
                    return false;

                var ctService = DentalCtVolumeService.EnsureInstance();
                var coordinator = ctService.GetComponent<DentalCtSliceCoordinator>();
                if (coordinator == null
                    || !coordinator.TryExecuteControl(sliceState, out executionDisposition, out executionError))
                    return false;

                var applied = state.ApplySliceState(sliceState);
                if (applied && RgbSliceGestureController.Instance != null)
                {
                    if (sliceState.Source == DentalControlSource.XrealGesture)
                        RgbSliceGestureController.Instance.ApplyAcknowledgedControlVersion(sliceState.ControlVersion);
                    else
                        RgbSliceGestureController.Instance.ApplyNavigationTakeover(sliceState.ControlVersion);
                }
                return applied;
            }, cancellationToken).ConfigureAwait(false);
            await SendAckAsync(
                writer,
                string.Empty,
                AckKind.SliceControl,
                sliceState.VolumeId,
                sliceState.ControlVersion,
                accepted,
                accepted
                    ? (executionDisposition == DentalSliceExecutionDisposition.PendingVolume
                        ? "slice state accepted; waiting for matching CT volume"
                        : "slice state applied")
                    : (string.IsNullOrEmpty(executionError)
                        ? "slice state rejected"
                        : "slice state rejected: " + executionError),
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleDisplayLayoutAsync(
            AsyncRequestWriter<ClientMessage> writer,
            DisplayLayout message,
            CancellationToken cancellationToken)
        {
            var layout = ToDomain(message);
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var stateApplied = DentalNavigationState.EnsureInstance().ApplyDisplayLayout(layout);
                if (!stateApplied)
                {
                    QueueDisplayState(layout, false, "display layout rejected before execution");
                    return false;
                }

                var controller = DentalDisplayLayoutController.EnsureInstance();
                var executed = controller.ApplyRemote(layout);
                QueueDisplayState(
                    controller.CaptureAppliedState(layout.Source),
                    executed,
                    executed ? "display layout applied" : "display layout execution failed");
                return executed;
            }, cancellationToken).ConfigureAwait(false);
            await SendAckAsync(
                writer,
                string.Empty,
                AckKind.DisplayLayout,
                "display_layout",
                layout.ControlVersion,
                accepted,
                accepted ? "display layout applied" : "display layout rejected",
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleObservationControlAsync(
            AsyncRequestWriter<ClientMessage> writer,
            ObservationControl message,
            CancellationToken cancellationToken)
        {
            if (message.ReceiverPort > int.MaxValue
                || message.Width > int.MaxValue
                || message.Height > int.MaxValue
                || message.Fps > int.MaxValue)
            {
                await SendAckAsync(
                    writer,
                    string.Empty,
                    AckKind.ObservationControl,
                    "observation_control",
                    message.ControlVersion,
                    false,
                    "observation control contains values outside the supported integer range",
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            var control = ToDomain(message);
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.EnsureInstance();
                if (!state.CanApplyObservationControl(control))
                    return false;

                var streamer = XrRgbRtpStreamer.Instance;
                if (streamer == null)
                {
                    var disabled = !control.MirrorEnabled && !control.RgbEnabled;
                    if (disabled)
                        state.ApplyObservationControl(control);
                    QueueObservationStatus(
                        control.SessionId,
                        control.ContextVersion,
                        control.ControlVersion,
                        false,
                        false,
                        disabled ? string.Empty : "XR/RGB streamer is not ready");
                    return disabled;
                }

                var executionOk = streamer.TryApplyControl(control, out var executionError);
                if (executionOk && !state.ApplyObservationControl(control))
                    return false;
                QueueObservationStatus(
                    control.SessionId,
                    control.ContextVersion,
                    control.ControlVersion,
                    control.MirrorEnabled && streamer.HasLiveXrFrame,
                    control.RgbEnabled && streamer.HasLiveRgbFrame,
                    executionOk ? streamer.SourceAvailabilityMessage : executionError);
                m_ObservationStatusPending = executionOk;
                return executionOk;
            }, cancellationToken).ConfigureAwait(false);
            await SendAckAsync(
                writer,
                string.Empty,
                AckKind.ObservationControl,
                "observation_control",
                control.ControlVersion,
                accepted,
                accepted ? "observation control applied" : "observation control rejected",
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleNavigationStatusAsync(NavigationStatus message, CancellationToken cancellationToken)
        {
            await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.EnsureInstance();
                var snapshot = state.Capture(Time.realtimeSinceStartup);
                if (!snapshot.HasContext
                    || snapshot.ContextVersion != message.ContextVersion
                    || !string.Equals(snapshot.SessionId, message.SessionId, StringComparison.Ordinal))
                    return;
                if (message.State == NavigationRunState.Active)
                    state.NotifyNavigationActive(message.Reason);
                else if (message.State == NavigationRunState.Paused)
                    state.NotifyNavigationStopped(string.IsNullOrEmpty(message.Reason) ? "导航已暂停" : message.Reason);
                else if (message.State == NavigationRunState.Stopped)
                    state.NotifyNavigationStopped(message.Reason);
            }, cancellationToken).ConfigureAwait(false);
        }

        async Task HandleSessionEndAsync(
            AsyncRequestWriter<ClientMessage> writer,
            SessionEnd message,
            CancellationToken cancellationToken)
        {
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.EnsureInstance();
                var snapshot = state.Capture(Time.realtimeSinceStartup);
                if (!snapshot.HasContext
                    || !string.Equals(snapshot.SessionId, message.SessionId, StringComparison.Ordinal))
                    return false;
                state.NotifySessionEnded(message.Reason);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                m_ServerSessionEnded = true;
                m_ReconnectAllowed = message.ReconnectAllowed;
            }
            await SendAckAsync(
                writer,
                string.Empty,
                AckKind.SessionEnd,
                message.SessionId,
                0,
                accepted,
                accepted ? "session end received" : "session end ignored for inactive session",
                cancellationToken).ConfigureAwait(false);
        }

        async Task SendAckAsync(
            AsyncRequestWriter<ClientMessage> writer,
            string datasetId,
            AckKind kind,
            string itemId,
            ulong version,
            bool accepted,
            string message,
            CancellationToken cancellationToken)
        {
            if (!m_SendAckMessages || writer == null)
                return;

            await writer.EnqueueAsync(new ClientMessage
            {
                Ack = new ClientAck
                {
                    DatasetId = datasetId ?? string.Empty,
                    SessionId = m_ServerSessionId ?? string.Empty,
                    Kind = kind,
                    ItemId = itemId ?? string.Empty,
                    Version = version,
                    Accepted = accepted,
                    Message = message ?? string.Empty,
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        async Task HandleAssetServerMessageAsync(
            AsyncRequestWriter<AssetClientMessage> writer,
            AssetServerMessage message,
            CancellationToken cancellationToken)
        {
            switch (message.PayloadCase)
            {
                case AssetServerMessage.PayloadOneofCase.Manifest:
                    await HandleAssetManifestAsync(writer, message.Manifest, cancellationToken).ConfigureAwait(false);
                    break;

                case AssetServerMessage.PayloadOneofCase.Chunk:
                    await HandleAssetChunkAsync(writer, message.Chunk, cancellationToken).ConfigureAwait(false);
                    break;

                case AssetServerMessage.PayloadOneofCase.Complete:
                    await HandleAssetTransferCompleteAsync(writer, message.Complete, cancellationToken).ConfigureAwait(false);
                    break;

                case AssetServerMessage.PayloadOneofCase.Heartbeat:
                    writer.TryEnqueue(new AssetClientMessage
                    {
                        Heartbeat = new Heartbeat
                        {
                            SessionId = message.Heartbeat.SessionId,
                            SentTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        }
                    });
                    break;

                case AssetServerMessage.PayloadOneofCase.SessionEnd:
                    await HandleAssetSessionEndAsync(message.SessionEnd, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        async Task HandleAssetSessionEndAsync(SessionEnd message, CancellationToken cancellationToken)
        {
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.Instance;
                var snapshot = state != null
                    ? state.Capture(Time.realtimeSinceStartup)
                    : default(DentalNavigationSnapshot);
                return snapshot.HasContext
                    && string.Equals(snapshot.SessionId, message.SessionId, StringComparison.Ordinal);
            }, cancellationToken).ConfigureAwait(false);
            if (!accepted)
                return;

            // SessionEnd on StreamAssets closes only this asset stream. When reconnect is
            // allowed, the protected loop opens a new stream and sends resumable offsets;
            // the realtime navigation session remains independent.
            m_AssetStreamEndReceived = true;
            m_AssetSessionEnded = !message.ReconnectAllowed;
            m_AssetTransferInProgress = false;
            EnqueueStatus(string.IsNullOrEmpty(message.Reason)
                ? (message.ReconnectAllowed ? "资产通道请求重连。" : "资产通道已结束。")
                : message.Reason);
        }

        async Task HandleAssetManifestAsync(
            AsyncRequestWriter<AssetClientMessage> writer,
            AssetManifest manifest,
            CancellationToken cancellationToken)
        {
            var copy = manifest.Clone();
            var manifestError = string.Empty;
            var initialOffsets = new Dictionary<string, long>(StringComparer.Ordinal);
            var dicomTransferNamespace = CreateDicomTransferNamespace(copy);
            var dicomTransferGeneration = 0L;
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var state = DentalNavigationState.Instance;
                if (state == null)
                    return false;

                var snapshot = state.Capture(Time.realtimeSinceStartup);
                if (string.IsNullOrEmpty(copy.TransferId) ||
                    !snapshot.HasContext ||
                    snapshot.ContextVersion != copy.ContextVersion ||
                    !string.Equals(snapshot.SessionId, copy.SessionId, StringComparison.Ordinal) ||
                    !string.Equals(snapshot.DatasetId, copy.DatasetId, StringComparison.Ordinal))
                {
                    manifestError = "asset manifest does not match the active navigation context";
                    return false;
                }

                var dicomDescriptors = new List<DicomTransferFileDescriptor>();
                var stlDescriptors = new List<AssetDescriptor>();
                var stlTypes = new HashSet<AssetType>();
                var assetIds = new HashSet<string>(StringComparer.Ordinal);
                var sopInstanceUids = new HashSet<string>(StringComparer.Ordinal);
                var dicomOrderIndexes = new HashSet<uint>();
                long totalAssetBytes = 0;
                if (copy.Assets.Count > MaxAssetCount)
                {
                    manifestError = $"asset manifest exceeds the {MaxAssetCount} item limit";
                    return false;
                }
                foreach (var descriptor in copy.Assets)
                {
                    if (string.IsNullOrEmpty(descriptor.AssetId) || !assetIds.Add(descriptor.AssetId))
                    {
                        manifestError = "asset manifest contains an empty or duplicate asset id";
                        return false;
                    }
                    if (!string.Equals(descriptor.DatasetId, copy.DatasetId, StringComparison.Ordinal))
                    {
                        manifestError = $"asset {descriptor.AssetId} does not match the manifest dataset";
                        return false;
                    }

                    if (descriptor.TotalBytes <= 0 || descriptor.Sha256 == null || descriptor.Sha256.Length != 32)
                    {
                        manifestError = $"asset {descriptor.AssetId} requires positive size and a 32-byte SHA-256";
                        return false;
                    }
                    if (descriptor.TotalBytes > MaxTotalAssetBytes - totalAssetBytes)
                    {
                        manifestError = $"asset manifest exceeds the {MaxTotalAssetBytes} byte total limit";
                        return false;
                    }
                    totalAssetBytes += descriptor.TotalBytes;

                    if (descriptor.AssetType == AssetType.StlTeeth || descriptor.AssetType == AssetType.StlDrill)
                    {
                        if (descriptor.TotalBytes > DentalRobotModelRenderer.MaxStlAssetBytes
                            || !stlTypes.Add(descriptor.AssetType))
                        {
                            manifestError = "each STL model type must occur once and fit a Unity byte buffer";
                            return false;
                        }
                        stlDescriptors.Add(descriptor);
                        continue;
                    }
                    if (descriptor.AssetType != AssetType.DicomInstance)
                    {
                        manifestError = $"asset {descriptor.AssetId} has an unsupported type";
                        return false;
                    }
                    if (descriptor.TotalBytes > MaxSingleDicomBytes)
                    {
                        manifestError = $"DICOM asset {descriptor.AssetId} exceeds the {MaxSingleDicomBytes} byte per-file limit";
                        return false;
                    }
                    if (string.IsNullOrEmpty(descriptor.SopInstanceUid)
                        || !sopInstanceUids.Add(descriptor.SopInstanceUid)
                        || descriptor.FrameCount == 0
                        || !dicomOrderIndexes.Add(descriptor.OrderIndex))
                    {
                        manifestError = $"DICOM asset {descriptor.AssetId} requires a unique SOP UID, frame count, and order index";
                        return false;
                    }
                    if (!string.Equals(descriptor.TransferSyntaxUid, ExplicitVrLittleEndianUid, StringComparison.Ordinal))
                    {
                        manifestError = "DICOM assets must use Explicit VR Little Endian";
                        return false;
                    }

                    var sha256Hex = ToHex(descriptor.Sha256);
                    var fileDescriptor = new DicomTransferFileDescriptor(
                        dicomTransferNamespace,
                        descriptor.AssetId,
                        descriptor.Filename,
                        descriptor.TotalBytes,
                        sha256Hex);
                    dicomDescriptors.Add(fileDescriptor);
                }

                var isNewTransfer = true;
                lock (m_AssetDescriptorLock)
                {
                    var sameIdentity = ActiveTransferMatchesLocked(
                        copy.TransferId,
                        copy.SessionId,
                        copy.DatasetId,
                        copy.ContextVersion);
                    if (sameIdentity && !ManifestDescriptorsMatchLocked(copy))
                    {
                        manifestError = "an active transfer id cannot be reused with different asset descriptors";
                        return false;
                    }
                    isNewTransfer = !sameIdentity;
                }
                if (isNewTransfer)
                {
                    m_AssetProgress.Clear();
                    m_CompletedDicomAssets.Clear();
                    m_CommittedTransferId = string.Empty;
                }

                if (dicomDescriptors.Count > 0)
                {
                    m_CtVolumeService = DentalCtVolumeService.EnsureInstance();
                    dicomTransferGeneration = m_CtVolumeService.ActivateTransferScope(dicomTransferNamespace);
                    if (isNewTransfer)
                        m_CtVolumeService.ClearVolume();

                    foreach (var descriptor in dicomDescriptors)
                    {
                        if (!m_CtVolumeService.BeginAsset(
                                dicomTransferGeneration,
                                dicomTransferNamespace,
                                descriptor,
                                out manifestError))
                            return false;
                        if (m_CtVolumeService.TryGetAssetProgress(
                                dicomTransferGeneration,
                                dicomTransferNamespace,
                                descriptor.AssetId,
                                out var progress))
                            initialOffsets[descriptor.AssetId] = progress.NextMissingOffset;
                    }
                }

                if (stlDescriptors.Count > 0)
                {
                    var renderer = DentalRobotModelRenderer.Instance;
                    if (renderer == null)
                    {
                        manifestError = "STL renderer is not initialized";
                        return false;
                    }
                    foreach (var descriptor in stlDescriptors)
                    {
                        if (!renderer.BeginStlAsset(ToRendererModelType(descriptor.AssetType),
                                descriptor.Filename, descriptor.TotalBytes))
                        {
                            manifestError = $"STL asset {descriptor.AssetId} could not be initialized";
                            return false;
                        }
                    }
                }

                AssetManifestReceived?.Invoke(copy);
                lock (m_AssetDescriptorLock)
                {
                    if (isNewTransfer)
                        m_ActiveTransferGeneration = unchecked(m_ActiveTransferGeneration + 1);
                    m_ActiveTransferId = copy.TransferId ?? string.Empty;
                    m_ActiveTransferSessionId = copy.SessionId ?? string.Empty;
                    m_ActiveTransferDatasetId = copy.DatasetId ?? string.Empty;
                    m_ActiveTransferContextVersion = copy.ContextVersion;
                    m_ActiveDicomTransferGeneration = dicomDescriptors.Count > 0
                        ? dicomTransferGeneration
                        : 0;
                    m_ActiveDicomTransferNamespace = dicomDescriptors.Count > 0
                        ? dicomTransferNamespace
                        : string.Empty;
                    m_AssetDescriptors.Clear();
                    foreach (var descriptor in copy.Assets)
                        m_AssetDescriptors[descriptor.AssetId ?? string.Empty] = descriptor.Clone();
                }
                m_AssetTransferInProgress = true;
                return true;
            }, cancellationToken).ConfigureAwait(false);
            if (!accepted)
            {
                await writer.EnqueueAsync(new AssetClientMessage
                {
                    ManifestAck = new AssetManifestAck
                    {
                        TransferId = copy.TransferId,
                        Accepted = false,
                        Message = manifestError,
                    }
                }, cancellationToken).ConfigureAwait(false);
                EnqueueStatus("资产清单被拒绝：" + manifestError);
                return;
            }

            foreach (var pair in initialOffsets)
            {
                var progress = m_AssetProgress.GetOrAdd(
                    AssetResumeKey(copy.TransferId, pair.Key),
                    _ => new AssetReceiveProgress());
                if (pair.Value > 0)
                    progress.Add(0, pair.Value);
            }

            await writer.EnqueueAsync(new AssetClientMessage
            {
                ManifestAck = new AssetManifestAck
                {
                    TransferId = copy.TransferId,
                    Accepted = true,
                    Message = "manifest accepted",
                }
            }, cancellationToken).ConfigureAwait(false);

            var resume = new AssetResumeRequest
            {
                TransferId = copy.TransferId,
                DatasetId = copy.DatasetId,
            };
            foreach (var descriptor in copy.Assets)
            {
                var resumeKey = AssetResumeKey(copy.TransferId, descriptor.AssetId);
                if (m_AssetProgress.TryGetValue(resumeKey, out var progress) && progress.NextMissingOffset > 0)
                {
                    resume.Assets.Add(new AssetResumeOffset
                    {
                        AssetId = descriptor.AssetId,
                        NextOffset = Math.Min(progress.NextMissingOffset, Math.Max(0, descriptor.TotalBytes)),
                    });
                }
            }

            if (resume.Assets.Count > 0)
                await writer.EnqueueAsync(new AssetClientMessage { Resume = resume }, cancellationToken).ConfigureAwait(false);
        }

        async Task HandleAssetChunkAsync(
            AsyncRequestWriter<AssetClientMessage> writer,
            AssetChunk chunk,
            CancellationToken cancellationToken)
        {
            var assetId = chunk.AssetId ?? string.Empty;
            var resumeKey = AssetResumeKey(chunk.TransferId, assetId);
            var data = chunk.Data == null ? Array.Empty<byte>() : chunk.Data.ToByteArray();
            var expectedOffset = 0L;
            if (data.Length == 0 || data.Length > MaxAssetChunkBytes || chunk.Offset < 0)
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "empty/oversized chunk or negative offset", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (chunk.Crc32 != 0 && Crc32.Compute(data) != chunk.Crc32)
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "crc32 mismatch", cancellationToken).ConfigureAwait(false);
                return;
            }

            AssetDescriptor descriptor;
            long transferGeneration;
            long dicomTransferGeneration;
            string dicomTransferNamespace;
            lock (m_AssetDescriptorLock)
            {
                if (!string.Equals(m_ActiveTransferId, chunk.TransferId, StringComparison.Ordinal))
                    descriptor = null;
                else
                m_AssetDescriptors.TryGetValue(assetId, out descriptor);
                transferGeneration = m_ActiveTransferGeneration;
                dicomTransferGeneration = m_ActiveDicomTransferGeneration;
                dicomTransferNamespace = m_ActiveDicomTransferNamespace;
            }

            if (descriptor == null)
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "asset is not present in the active manifest", cancellationToken).ConfigureAwait(false);
                return;
            }

            var progress = m_AssetProgress.GetOrAdd(resumeKey, _ => new AssetReceiveProgress());
            expectedOffset = progress.NextMissingOffset;

            if (descriptor.TotalBytes < 0 || chunk.Offset > descriptor.TotalBytes - data.LongLength)
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "chunk exceeds declared asset size", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (descriptor != null && descriptor.AssetType == AssetType.DicomInstance &&
                !string.Equals(descriptor.TransferSyntaxUid, ExplicitVrLittleEndianUid, StringComparison.Ordinal))
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "unsupported DICOM transfer syntax", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (progress.Covers(chunk.Offset, data.LongLength))
            {
                await SendAssetAckAsync(writer, chunk, progress.NextMissingOffset, true, "duplicate chunk", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (descriptor.AssetType == AssetType.DicomInstance)
            {
                var ctService = m_CtVolumeService;
                if (ctService == null)
                {
                    await SendAssetAckAsync(writer, chunk, expectedOffset, false, "DICOM sink is not initialized", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var writeResult = await Task.Run(
                    () => ctService.WriteAssetChunkBackground(
                        dicomTransferGeneration,
                        dicomTransferNamespace,
                        assetId,
                        chunk.Offset,
                        data),
                    cancellationToken).ConfigureAwait(false);
                if (!writeResult.Success)
                {
                    await SendAssetAckAsync(writer, chunk, expectedOffset, false, writeResult.Error, cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (!ActiveTransferGenerationMatches(chunk.TransferId, transferGeneration))
                {
                    await SendAssetAckAsync(writer, chunk, 0, false, "asset context changed while the chunk was being written", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var copy = chunk.Clone();
            var dispatched = await EnqueueMainThreadAsync(() =>
            {
                if (!ActiveTransferGenerationMatches(chunk.TransferId, transferGeneration))
                    return false;
                AssetChunkReceived?.Invoke(copy);
                return ApplyStlAssetChunkToLegacyDisplay(descriptor, copy, data);
            }, cancellationToken).ConfigureAwait(false);
            if (!dispatched)
            {
                await SendAssetAckAsync(writer, chunk, expectedOffset, false, "asset consumer failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!ActiveTransferGenerationMatches(chunk.TransferId, transferGeneration))
            {
                await SendAssetAckAsync(writer, chunk, 0, false, "asset context changed before the chunk could be committed", cancellationToken).ConfigureAwait(false);
                return;
            }

            progress.Add(chunk.Offset, data.LongLength);
            var nextOffset = progress.NextMissingOffset;
            await SendAssetAckAsync(writer, chunk, nextOffset, true, string.Empty, cancellationToken).ConfigureAwait(false);
        }

        async Task HandleAssetTransferCompleteAsync(
            AsyncRequestWriter<AssetClientMessage> writer,
            AssetTransferComplete complete,
            CancellationToken cancellationToken)
        {
            var copy = complete.Clone();
            var dicomAssets = new List<AssetDescriptor>();
            var descriptors = new List<AssetDescriptor>();
            var transferGeneration = 0L;
            var dicomTransferGeneration = 0L;
            var dicomTransferNamespace = string.Empty;
            lock (m_AssetDescriptorLock)
            {
                if (!ActiveTransferMatchesLocked(
                        copy.TransferId,
                        copy.SessionId,
                        copy.DatasetId,
                        copy.ContextVersion))
                {
                    descriptors = null;
                }
                else
                {
                transferGeneration = m_ActiveTransferGeneration;
                dicomTransferGeneration = m_ActiveDicomTransferGeneration;
                dicomTransferNamespace = m_ActiveDicomTransferNamespace;
                foreach (var descriptor in m_AssetDescriptors.Values)
                {
                    descriptors.Add(descriptor.Clone());
                    if (descriptor.AssetType == AssetType.DicomInstance)
                        dicomAssets.Add(descriptor.Clone());
                }
                }
            }
            if (descriptors == null)
            {
                await writer.EnqueueAsync(new AssetClientMessage
                {
                    CompleteAck = new AssetCompleteAck
                    {
                        TransferId = copy.TransferId,
                        Accepted = false,
                        Message = "completion does not match the active asset manifest",
                    }
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            for (var i = 0; copy.Ok && i < copy.Results.Count; i++)
            {
                if (!copy.Results[i].Ok)
                {
                    copy.Ok = false;
                    copy.Message = string.IsNullOrEmpty(copy.Results[i].Message)
                        ? $"asset {copy.Results[i].AssetId} failed on server"
                        : copy.Results[i].Message;
                }
            }

            var completionResult = copy.Ok
                ? ValidateAssetCoverage(copy.TransferId, descriptors)
                : new DicomOperationResult(false, string.IsNullOrEmpty(copy.Message) ? "server reported asset transfer failure" : copy.Message);
            if (completionResult.Success)
                completionResult = await CompleteDicomAssetsOnMainThreadAsync(
                    copy.TransferId,
                    copy.SessionId,
                    copy.DatasetId,
                    copy.ContextVersion,
                    transferGeneration,
                    dicomTransferGeneration,
                    dicomTransferNamespace,
                    dicomAssets,
                    cancellationToken).ConfigureAwait(false);
            if (completionResult.Success)
                completionResult = await VerifyStlAssetsAsync(descriptors, cancellationToken).ConfigureAwait(false);
            if (completionResult.Success && !ActiveTransferMatches(copy))
                completionResult = new DicomOperationResult(false, "asset context changed while completion was being verified");
            copy.Ok = copy.Ok && completionResult.Success;
            if (!completionResult.Success)
                copy.Message = completionResult.Error;

            var notified = await EnqueueMainThreadAsync(() =>
            {
                lock (m_AssetDescriptorLock)
                {
                    if (!ActiveTransferMatchesLocked(
                            copy.TransferId,
                            copy.SessionId,
                            copy.DatasetId,
                            copy.ContextVersion))
                        return false;
                }
                AssetTransferCompleted?.Invoke(copy);
                ApplyAssetCompletionToLegacyDisplay(copy);
                DentalNavigationState.EnsureInstance().NotifyModelTransfer(copy.Ok, copy.Message);
                m_AssetTransferInProgress = false;
                return true;
            }, cancellationToken).ConfigureAwait(false);
            if (!notified)
            {
                copy.Ok = false;
                copy.Message = "asset context changed before completion could be applied";
            }
            await writer.EnqueueAsync(new AssetClientMessage
            {
                CompleteAck = new AssetCompleteAck
                {
                    TransferId = copy.TransferId,
                    Accepted = copy.Ok,
                    Message = copy.Ok ? "assets verified and committed" : copy.Message,
                }
            }, cancellationToken).ConfigureAwait(false);
            // Asset completion intentionally leaves both streams open.
        }

        async Task SendAssetAckAsync(
            AsyncRequestWriter<AssetClientMessage> writer,
            AssetChunk chunk,
            long nextOffset,
            bool accepted,
            string message,
            CancellationToken cancellationToken)
        {
            await writer.EnqueueAsync(new AssetClientMessage
            {
                ChunkAck = new AssetChunkAck
                {
                    TransferId = chunk.TransferId ?? string.Empty,
                    AssetId = chunk.AssetId ?? string.Empty,
                    NextOffset = nextOffset,
                    Accepted = accepted,
                    Message = message ?? string.Empty,
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        async Task<DicomOperationResult> CompleteDicomAssetsOnMainThreadAsync(
            string transferId,
            string sessionId,
            string datasetId,
            ulong contextVersion,
            long transferGeneration,
            long dicomTransferGeneration,
            string dicomTransferNamespace,
            List<AssetDescriptor> assets,
            CancellationToken cancellationToken)
        {
            if (assets == null || assets.Count == 0)
                return new DicomOperationResult(true, string.Empty);

            var completion = new TaskCompletionSource<DicomOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await EnqueueMainThreadAsync(() =>
            {
                if (!ActiveTransferIdentityGenerationMatches(
                        transferId, sessionId, datasetId, contextVersion, transferGeneration))
                {
                    completion.TrySetResult(new DicomOperationResult(
                        false,
                        "asset context changed before DICOM completion could start"));
                    return;
                }
                _ = CompleteDicomAssetsAsync(
                    transferId,
                    sessionId,
                    datasetId,
                    contextVersion,
                    transferGeneration,
                    dicomTransferGeneration,
                    dicomTransferNamespace,
                    assets,
                    completion);
            }, cancellationToken).ConfigureAwait(false);

            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                return await completion.Task.ConfigureAwait(false);
        }

        DicomOperationResult ValidateAssetCoverage(string transferId, List<AssetDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var key = AssetResumeKey(transferId, descriptor.AssetId);
                if (!m_AssetProgress.TryGetValue(key, out var progress)
                    || !progress.Covers(0, descriptor.TotalBytes))
                {
                    var next = progress != null ? progress.NextMissingOffset : 0;
                    return new DicomOperationResult(false,
                        $"asset {descriptor.AssetId} is incomplete; next missing offset={next}");
                }
            }
            return new DicomOperationResult(true, string.Empty);
        }

        async Task<DicomOperationResult> VerifyStlAssetsAsync(
            List<AssetDescriptor> descriptors,
            CancellationToken cancellationToken)
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.AssetType != AssetType.StlTeeth && descriptor.AssetType != AssetType.StlDrill)
                    continue;

                byte[] bytes = null;
                var error = string.Empty;
                var copied = await EnqueueMainThreadAsync(() =>
                {
                    var renderer = DentalRobotModelRenderer.Instance;
                    return renderer != null && renderer.TryCopyStlAssetBytes(
                        ToRendererModelType(descriptor.AssetType), descriptor.TotalBytes, out bytes, out error);
                }, cancellationToken).ConfigureAwait(false);
                if (!copied)
                    return new DicomOperationResult(false,
                        string.IsNullOrEmpty(error) ? $"STL asset {descriptor.AssetId} is unavailable" : error);

                var expectedHash = ToHex(descriptor.Sha256);
                var actualHash = await Task.Run(() =>
                {
                    using (var sha = SHA256.Create())
                        return ToHex(ByteString.CopyFrom(sha.ComputeHash(bytes)));
                }, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                    return new DicomOperationResult(false,
                        $"STL SHA-256 mismatch for {descriptor.AssetId}; expected={expectedHash}, actual={actualHash}");
            }
            return new DicomOperationResult(true, string.Empty);
        }

        async Task CompleteDicomAssetsAsync(
            string transferId,
            string sessionId,
            string datasetId,
            ulong contextVersion,
            long transferGeneration,
            long dicomTransferGeneration,
            string dicomTransferNamespace,
            List<AssetDescriptor> assets,
            TaskCompletionSource<DicomOperationResult> completion)
        {
            try
            {
                var service = m_CtVolumeService;
                if (service == null)
                {
                    completion.TrySetResult(new DicomOperationResult(false, "DICOM sink is not initialized."));
                    return;
                }
                if (!ActiveTransferIdentityGenerationMatches(
                        transferId, sessionId, datasetId, contextVersion, transferGeneration))
                {
                    completion.TrySetResult(new DicomOperationResult(false, "asset context changed during DICOM completion"));
                    return;
                }

                if (string.Equals(m_CommittedTransferId, transferId, StringComparison.Ordinal))
                {
                    completion.TrySetResult(new DicomOperationResult(true, string.Empty));
                    return;
                }

                assets.Sort((left, right) =>
                {
                    var order = left.OrderIndex.CompareTo(right.OrderIndex);
                    return order != 0 ? order : string.CompareOrdinal(left.AssetId, right.AssetId);
                });
                foreach (var descriptor in assets)
                {
                    if (!ActiveTransferIdentityGenerationMatches(
                            transferId, sessionId, datasetId, contextVersion, transferGeneration))
                    {
                        completion.TrySetResult(new DicomOperationResult(false, "asset context changed during DICOM completion"));
                        return;
                    }
                    var assetId = descriptor.AssetId ?? string.Empty;
                    if (m_CompletedDicomAssets.Contains(assetId))
                        continue;

                    var expectation = new DicomAssetExpectation(
                        descriptor.SopInstanceUid,
                        descriptor.FrameCount,
                        descriptor.OrderIndex);
                    var result = await service.CompleteAssetAsync(
                        dicomTransferGeneration,
                        dicomTransferNamespace,
                        assetId,
                        expectation);
                    if (!result.Success)
                    {
                        completion.TrySetResult(result);
                        return;
                    }
                    if (!ActiveTransferIdentityGenerationMatches(
                            transferId, sessionId, datasetId, contextVersion, transferGeneration))
                    {
                        completion.TrySetResult(new DicomOperationResult(false, "asset context changed during DICOM completion"));
                        return;
                    }
                    m_CompletedDicomAssets.Add(assetId);
                }

                if (!ActiveTransferIdentityGenerationMatches(
                        transferId, sessionId, datasetId, contextVersion, transferGeneration))
                {
                    completion.TrySetResult(new DicomOperationResult(false, "asset context changed before DICOM volume commit"));
                    return;
                }
                var commitResult = await service.CommitVolumeAsync(
                    dicomTransferGeneration,
                    dicomTransferNamespace);
                if (commitResult.Success && ActiveTransferIdentityGenerationMatches(
                        transferId, sessionId, datasetId, contextVersion, transferGeneration))
                    m_CommittedTransferId = transferId ?? string.Empty;
                else if (commitResult.Success)
                    commitResult = new DicomOperationResult(false, "asset context changed while the DICOM volume was committed");
                completion.TrySetResult(commitResult);
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new DicomOperationResult(false, ex.Message));
            }
        }

        async Task HandleLegacyStlChunkAsync(StlChunk chunk, CancellationToken cancellationToken)
        {
            var modelType = chunk.ModelType.ToString();
            var datasetId = chunk.DatasetId;
            var filename = chunk.Filename;
            var offset = chunk.Offset;
            var data = chunk.Data == null ? Array.Empty<byte>() : chunk.Data.ToByteArray();
            await EnqueueMainThreadAsync(() =>
            {
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.ApplyStlChunk(datasetId, modelType, filename, offset, data.Length, data);
            }, cancellationToken).ConfigureAwait(false);
        }

        async Task HandleLegacyTransferEndAsync(TransferEnd end, CancellationToken cancellationToken)
        {
            var copy = end.Clone();
            await EnqueueMainThreadAsync(() =>
            {
                DentalNavigationState.EnsureInstance().NotifyModelTransfer(copy.Ok, copy.Message);
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.ApplyTransferEnd(copy.DatasetId, copy.Ok, copy.Message, copy.TeethBytes, copy.DrillBytes);
            }, cancellationToken).ConfigureAwait(false);
        }

        bool ApplyStlAssetChunkToLegacyDisplay(AssetDescriptor descriptor, AssetChunk chunk, byte[] data)
        {
            if (descriptor == null ||
                (descriptor.AssetType != AssetType.StlTeeth && descriptor.AssetType != AssetType.StlDrill))
                return true;

            var display = DentalRobotBeamProDisplay.Instance;
            if (display != null)
            {
                var modelType = descriptor.AssetType == AssetType.StlTeeth ? ModelType.Teeth.ToString() : ModelType.Drill.ToString();
                return display.ApplyStlChunk(
                    descriptor.DatasetId,
                    modelType,
                    descriptor.Filename,
                    chunk.Offset,
                    data.Length,
                    data);
            }

            var renderer = DentalRobotModelRenderer.Instance;
            return renderer != null && renderer.TryApplyStlChunk(
                ToRendererModelType(descriptor.AssetType), descriptor.Filename, chunk.Offset, data);
        }

        void ApplyAssetCompletionToLegacyDisplay(AssetTransferComplete complete)
        {
            long teethBytes = 0;
            long drillBytes = 0;
            var hasStl = false;
            lock (m_AssetDescriptorLock)
            {
                foreach (var descriptor in m_AssetDescriptors.Values)
                {
                    if (descriptor.AssetType == AssetType.StlTeeth)
                    {
                        hasStl = true;
                        teethBytes += Math.Max(0, descriptor.TotalBytes);
                    }
                    else if (descriptor.AssetType == AssetType.StlDrill)
                    {
                        hasStl = true;
                        drillBytes += Math.Max(0, descriptor.TotalBytes);
                    }
                }
            }

            if (!hasStl)
                return;

            var display = DentalRobotBeamProDisplay.Instance;
            if (display != null)
                display.ApplyTransferEnd(complete.DatasetId, complete.Ok, complete.Message, teethBytes, drillBytes);
            else if (complete.Ok)
                DentalRobotModelRenderer.Instance?.ApplyTransferEnd(teethBytes, drillBytes);
        }

        void StoreLatestNavigationFrame(NavigationFrame frame)
        {
            if (frame == null)
                return;

            lock (m_LatestFrameLock)
            {
                var sameSession = string.Equals(m_LatestFrameSessionId, frame.SessionId, StringComparison.Ordinal);
                if (m_HasLatestFrameSequence && sameSession)
                {
                    if (frame.ContextVersion < m_LatestFrameContextVersion)
                        return;
                    if (frame.ContextVersion == m_LatestFrameContextVersion && frame.Sequence <= m_LatestFrameSequence)
                        return;
                }

                m_LatestFrame = frame.Clone();
                m_HasLatestFrame = true;
                m_HasLatestFrameSequence = true;
                m_LatestFrameSessionId = frame.SessionId ?? string.Empty;
                m_LatestFrameContextVersion = frame.ContextVersion;
                m_LatestFrameSequence = frame.Sequence;
            }
        }

        void ApplyLatestNavigationFrame()
        {
            NavigationFrame frame;
            lock (m_LatestFrameLock)
            {
                if (!m_HasLatestFrame)
                    return;

                frame = m_LatestFrame;
                m_LatestFrame = null;
                m_HasLatestFrame = false;
            }

            var unitsValid = frame.DistanceUnit == DistanceUnit.Millimeter && frame.AngleUnit == AngleUnit.Degree;
            var data = ToDomain(frame, unitsValid);
            DentalNavigationState.EnsureInstance().ApplyNavigationFrame(data);
        }

        void StoreLatestLegacyMetadata(ModelMetadata metadata)
        {
            if (metadata == null || m_HasV2Context)
                return;

            lock (m_LatestLegacyMetadataLock)
            {
                m_LatestLegacyMetadata = metadata.Clone();
                m_HasLatestLegacyMetadata = true;
            }
        }

        void ApplyLatestLegacyMetadata()
        {
            ModelMetadata metadata;
            lock (m_LatestLegacyMetadataLock)
            {
                if (!m_HasLatestLegacyMetadata)
                    return;

                metadata = m_LatestLegacyMetadata;
                m_LatestLegacyMetadata = null;
                m_HasLatestLegacyMetadata = false;
            }

            DentalNavigationState.EnsureInstance().ApplyMetadata(
                metadata.DatasetId,
                metadata.DrillFromTeeth,
                metadata.Distance,
                metadata.LateralDistance,
                metadata.Angle);
        }

        void ResetLatestFrames()
        {
            lock (m_LatestFrameLock)
            {
                m_LatestFrame = null;
                m_HasLatestFrame = false;
                m_HasLatestFrameSequence = false;
                m_LatestFrameSessionId = string.Empty;
                m_LatestFrameContextVersion = 0;
                m_LatestFrameSequence = 0;
            }

            lock (m_LatestLegacyMetadataLock)
            {
                m_LatestLegacyMetadata = null;
                m_HasLatestLegacyMetadata = false;
            }
        }

        void QueueAssetRequestForContext(DentalNavigationContext context, bool resetActiveTransfer)
        {
            if (resetActiveTransfer)
            {
                m_AssetTransferInProgress = false;
                lock (m_AssetDescriptorLock)
                {
                    m_ActiveTransferId = string.Empty;
                    m_ActiveTransferSessionId = string.Empty;
                    m_ActiveTransferDatasetId = string.Empty;
                    m_ActiveTransferContextVersion = 0;
                    m_ActiveTransferGeneration = unchecked(m_ActiveTransferGeneration + 1);
                    m_ActiveDicomTransferGeneration = 0;
                    m_ActiveDicomTransferNamespace = string.Empty;
                    m_AssetDescriptors.Clear();
                }
                m_AssetProgress.Clear();
                m_CompletedDicomAssets.Clear();
                m_CommittedTransferId = string.Empty;
                var ctService = m_CtVolumeService != null
                    ? m_CtVolumeService
                    : DentalCtVolumeService.Instance;
                if (ctService != null)
                    ctService.InvalidateTransferScopeAndClearVolume();
            }

            lock (m_PendingAssetContextLock)
            {
                m_PendingAssetContext = context;
                m_HasPendingAssetContext = true;
                m_HasAssetContext = true;
            }

            var writer = Volatile.Read(ref m_AssetWriter);
            if (writer != null)
                TrySendPendingAssetRequest(writer);
        }

        void TrySendPendingAssetRequest(AsyncRequestWriter<AssetClientMessage> writer)
        {
            lock (m_PendingAssetContextLock)
            {
                if (!m_HasPendingAssetContext)
                    return;

                var context = m_PendingAssetContext;
                if (writer.TryEnqueue(CreateAssetRequest(m_DeviceId, context.SessionId, context.DatasetId, context.ContextVersion)))
                    m_HasPendingAssetContext = false;
            }
        }

        void RequeueLastAssetRequest()
        {
            lock (m_PendingAssetContextLock)
            {
                if (m_HasAssetContext)
                    m_HasPendingAssetContext = true;
            }
        }

        static AssetClientMessage CreateAssetRequest(
            string deviceId,
            string sessionId,
            string datasetId,
            ulong contextVersion)
        {
            var request = new AssetRequest
            {
                DeviceId = deviceId ?? string.Empty,
                SessionId = sessionId ?? string.Empty,
                DatasetId = datasetId ?? string.Empty,
                ContextVersion = contextVersion,
                IncludeAll = true,
            };
            request.AcceptedTransferSyntaxUids.Add(ExplicitVrLittleEndianUid);
            return new AssetClientMessage { Request = request };
        }

        static string AssetResumeKey(string transferId, string assetId)
        {
            return (transferId ?? string.Empty) + "\n" + (assetId ?? string.Empty);
        }

        static string CreateDicomTransferNamespace(AssetManifest manifest)
        {
            var identity = new StringBuilder();
            AppendLengthPrefixed(identity, manifest?.SessionId);
            AppendLengthPrefixed(identity, manifest?.DatasetId);
            AppendLengthPrefixed(identity, manifest == null
                ? string.Empty
                : manifest.ContextVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendLengthPrefixed(identity, manifest?.TransferId);
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString()));
                return "dicom-transfer-v1-" + ToHex(ByteString.CopyFrom(digest));
            }
        }

        static void AppendLengthPrefixed(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length).Append(':').Append(value);
        }

        bool ActiveTransferMatches(AssetTransferComplete complete)
        {
            lock (m_AssetDescriptorLock)
            {
                return ActiveTransferMatchesLocked(
                    complete.TransferId,
                    complete.SessionId,
                    complete.DatasetId,
                    complete.ContextVersion);
            }
        }

        bool ActiveTransferMatchesLocked(
            string transferId,
            string sessionId,
            string datasetId,
            ulong contextVersion)
        {
            return !string.IsNullOrEmpty(m_ActiveTransferId)
                && string.Equals(m_ActiveTransferId, transferId, StringComparison.Ordinal)
                && string.Equals(m_ActiveTransferSessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(m_ActiveTransferDatasetId, datasetId, StringComparison.Ordinal)
                && m_ActiveTransferContextVersion == contextVersion;
        }

        bool ActiveTransferGenerationMatches(string transferId, long generation)
        {
            lock (m_AssetDescriptorLock)
            {
                return generation == m_ActiveTransferGeneration
                    && !string.IsNullOrEmpty(m_ActiveTransferId)
                    && string.Equals(m_ActiveTransferId, transferId, StringComparison.Ordinal);
            }
        }

        bool ActiveTransferIdentityGenerationMatches(
            string transferId,
            string sessionId,
            string datasetId,
            ulong contextVersion,
            long generation)
        {
            lock (m_AssetDescriptorLock)
            {
                return generation == m_ActiveTransferGeneration &&
                       ActiveTransferMatchesLocked(
                           transferId,
                           sessionId,
                           datasetId,
                           contextVersion);
            }
        }

        bool ManifestDescriptorsMatchLocked(AssetManifest manifest)
        {
            if (manifest.Assets.Count != m_AssetDescriptors.Count)
                return false;
            foreach (var candidate in manifest.Assets)
            {
                if (!m_AssetDescriptors.TryGetValue(candidate.AssetId ?? string.Empty, out var active)
                    || active.AssetType != candidate.AssetType
                    || active.TotalBytes != candidate.TotalBytes
                    || active.FrameCount != candidate.FrameCount
                    || active.OrderIndex != candidate.OrderIndex
                    || !string.Equals(active.DatasetId, candidate.DatasetId, StringComparison.Ordinal)
                    || !string.Equals(active.Filename, candidate.Filename, StringComparison.Ordinal)
                    || !string.Equals(active.RelativePath, candidate.RelativePath, StringComparison.Ordinal)
                    || !string.Equals(active.MediaType, candidate.MediaType, StringComparison.Ordinal)
                    || !string.Equals(active.TransferSyntaxUid, candidate.TransferSyntaxUid, StringComparison.Ordinal)
                    || !string.Equals(active.SopInstanceUid, candidate.SopInstanceUid, StringComparison.Ordinal)
                    || !Equals(active.Sha256, candidate.Sha256))
                    return false;
            }
            return true;
        }

        static string ToHex(ByteString bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        bool TryQueueSessionMessage(ClientMessage message)
        {
            var writer = Volatile.Read(ref m_SessionWriter);
            return writer != null && writer.TryEnqueue(message);
        }

        ulong NextCommandSequence()
        {
            return unchecked((ulong)Interlocked.Increment(ref m_CommandSequence));
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

        Task EnqueueMainThreadAsync(Action action, CancellationToken cancellationToken)
        {
            return m_MainThreadActions.EnqueueAsync(action, cancellationToken);
        }

        Task<bool> EnqueueMainThreadAsync(Func<bool> action, CancellationToken cancellationToken)
        {
            return m_MainThreadActions.EnqueueAsync(action, cancellationToken);
        }

        bool TryEnqueueMainThread(Action action)
        {
            return m_MainThreadActions.TryEnqueue(action);
        }

        void EnqueueStatus(string status)
        {
            TryEnqueueMainThread(() =>
            {
                var display = DentalRobotBeamProDisplay.Instance;
                if (display != null)
                    display.SetConnectionStatus(status);

                Debug.Log($"DentalRobotGrpcClient: {status}");
            });
        }

        void EnqueueLinkConnecting(string status)
        {
            TryEnqueueMainThread(() =>
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
            TryEnqueueMainThread(() =>
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
            TryEnqueueMainThread(() => Debug.LogException(exception));
        }

        void BindObservationStreamer()
        {
            var current = XrRgbRtpStreamer.Instance;
            if (current == m_ObservationStreamer)
                return;
            UnbindObservationStreamer();
            m_ObservationStreamer = current;
            if (m_ObservationStreamer != null)
                m_ObservationStreamer.StatusChanged += OnObservationStreamerStatusChanged;
            m_ObservationStatusPending = true;
        }

        void UnbindObservationStreamer()
        {
            if (m_ObservationStreamer != null)
                m_ObservationStreamer.StatusChanged -= OnObservationStreamerStatusChanged;
            m_ObservationStreamer = null;
        }

        void OnObservationStreamerStatusChanged(string _)
        {
            m_ObservationStatusPending = true;
        }

        void PumpObservationStatus()
        {
            var now = Time.realtimeSinceStartup;
            if (now < m_NextObservationStatusRealtime)
                return;
            m_NextObservationStatusRealtime = now + 0.25f;

            var state = DentalNavigationState.Instance;
            var snapshot = state != null
                ? state.Capture(now)
                : default(DentalNavigationSnapshot);
            if (!snapshot.HasObservationControl)
            {
                m_ObservationStatusPending = false;
                return;
            }

            var control = snapshot.ObservationControl;
            var streamer = m_ObservationStreamer;
            var mirrorAvailable = control.MirrorEnabled
                && streamer != null
                && streamer.IsStreaming
                && streamer.HasLiveXrFrame;
            var rgbAvailable = control.RgbEnabled
                && streamer != null
                && streamer.IsStreaming
                && streamer.HasLiveRgbFrame;
            var error = string.Empty;
            if (streamer != null && !string.IsNullOrEmpty(streamer.FaultMessage))
                error = streamer.FaultMessage;
            else if ((control.MirrorEnabled || control.RgbEnabled)
                && (streamer == null || !streamer.IsStreaming))
                error = streamer == null ? "XR/RGB streamer is not ready" : streamer.Status;
            else if (streamer != null)
                error = streamer.SourceAvailabilityMessage;

            var signature = control.ControlVersion + "|"
                + (mirrorAvailable ? "1" : "0") + "|"
                + (rgbAvailable ? "1" : "0") + "|" + error;
            if (!m_ObservationStatusPending
                && string.Equals(signature, m_LastObservationStatusSignature, StringComparison.Ordinal))
                return;

            if (QueueObservationStatus(mirrorAvailable, rgbAvailable, error))
            {
                m_LastObservationStatusSignature = signature;
                m_ObservationStatusPending = false;
            }
            else
            {
                m_ObservationStatusPending = true;
            }
        }

        static DentalNavigationContext ToDomain(NavigationContext message)
        {
            // A non-empty list means the sender supplied a transform. The state validator then
            // rejects any count other than exactly 16, any non-finite value, or a non-rigid matrix.
            var hasPatientFromDicom = message.PatientFromDicom != null && message.PatientFromDicom.Count > 0;
            return new DentalNavigationContext(
                message.SessionId,
                message.CaseId,
                message.DatasetId,
                message.CtId,
                message.PlanId,
                message.ToothId,
                message.ToolId,
                message.StepId,
                message.ContextVersion,
                ToUnityVector(message.PlanEntryMm),
                ToUnityVector(message.PlanAxis),
                (float)message.TargetDepthMm,
                ToUnityVector(message.BuccalAxis),
                ToUnityVector(message.MesialAxis),
                message.CoordinateFrameId,
                hasPatientFromDicom,
                DentalNavigationState.ToUnityMatrix(message.PatientFromDicom));
        }

        static DentalNavigationFrameData ToDomain(NavigationFrame message, bool unitsValid)
        {
            var hasMatrix = message.DrillFromTeeth != null && message.DrillFromTeeth.Count > 0;
            return new DentalNavigationFrameData(
                message.SessionId,
                message.ContextVersion,
                message.Sequence,
                message.CaptureTimeUnixMs,
                message.Valid && unitsValid,
                unitsValid ? message.InvalidReason : "导航单位必须为 mm 和 °",
                ToUnityVector(message.DrillTipMm),
                ToUnityVector(message.DrillAxis),
                hasMatrix,
                DentalNavigationState.ToUnityMatrix(message.DrillFromTeeth),
                (float)message.LateralMm,
                (float)message.LateralBuccalMm,
                (float)message.LateralMesialMm,
                (float)message.AngleDeg,
                (float)message.TiltBuccalDeg,
                (float)message.TiltMesialDeg,
                (float)message.CurrentDepthMm,
                (float)message.TargetDepthMm,
                (float)message.RemainingDepthMm,
                message.HasLateralDirection,
                message.HasTiltDirection,
                message.HasDepthBreakdown);
        }

        static DentalNavigationThresholds ToDomain(ToleranceConfig message)
        {
            return new DentalNavigationThresholds(
                message.SessionId,
                message.ContextVersion,
                message.ConfigVersion,
                (float)message.LateralGreenMaxMm,
                (float)message.LateralRedMinMm,
                (float)message.LateralHysteresisMm,
                (float)message.AngleGreenMaxDeg,
                (float)message.AngleRedMinDeg,
                (float)message.AngleHysteresisDeg,
                (float)message.DepthApproachMm,
                (float)message.DepthAtTargetToleranceMm,
                (float)message.DepthOverrunRedMm,
                (float)message.DepthHysteresisMm,
                ToDomain(message.BoundaryRule));
        }

        static DentalSliceState ToDomain(SliceState message)
        {
            var plane = message.Plane;
            return new DentalSliceState(
                message.SessionId,
                message.ContextVersion,
                message.ControlVersion,
                message.SyncEnabled,
                ToDomain(message.Source),
                plane != null && plane.HasPhysicalPlane,
                plane == null ? string.Empty : plane.VolumeId,
                plane == null ? string.Empty : plane.FrameOfReferenceUid,
                plane == null ? Vector3.zero : ToUnityVector(plane.OriginMm),
                plane == null ? Vector3.zero : ToUnityVector(plane.Normal),
                plane == null ? Vector3.zero : ToUnityVector(plane.Up),
                plane == null ? 0f : (float)plane.OffsetMm,
                plane == null ? 0 : plane.SliceIndex,
                plane == null ? string.Empty : plane.SopInstanceUid);
        }

        static DentalDisplayLayoutState ToDomain(DisplayLayout message)
        {
            return new DentalDisplayLayoutState(
                message.SessionId,
                message.ContextVersion,
                message.ControlVersion,
                ToDomain(message.Source),
                message.HudVisible,
                message.ModelVisible,
                ToUnityVector(message.HudPositionM),
                ToUnityVector(message.ModelPositionM),
                message.ResetToDefault);
        }

        static DentalObservationControlState ToDomain(ObservationControl message)
        {
            return new DentalObservationControlState(
                message.SessionId,
                message.ContextVersion,
                message.ControlVersion,
                message.XrMirrorEnabled,
                message.RgbEnabled,
                message.ReceiverHost,
                checked((int)message.ReceiverPort),
                checked((int)message.Width),
                checked((int)message.Height),
                checked((int)message.Fps));
        }

        static DisplayLayout ToProto(DentalDisplayLayoutState layout)
        {
            return new DisplayLayout
            {
                SessionId = layout.SessionId ?? string.Empty,
                ContextVersion = layout.ContextVersion,
                ControlVersion = layout.ControlVersion,
                Source = ToProto(layout.Source),
                HudVisible = layout.HudVisible,
                ModelVisible = layout.ModelVisible,
                HudPositionM = ToProtoVector(layout.HudPositionMeters),
                ModelPositionM = ToProtoVector(layout.ModelPositionMeters),
                ResetToDefault = layout.ResetToDefault,
            };
        }

        static DentalThresholdBoundaryRule ToDomain(ThresholdBoundaryRule value)
        {
            switch (value)
            {
                case ThresholdBoundaryRule.UpperBoundsInclusive:
                    return DentalThresholdBoundaryRule.UpperBoundsInclusive;
                case ThresholdBoundaryRule.UpperBoundsExclusive:
                    return DentalThresholdBoundaryRule.UpperBoundsExclusive;
                default:
                    return DentalThresholdBoundaryRule.Unspecified;
            }
        }

        static DentalControlSource ToDomain(ControlSource value)
        {
            switch (value)
            {
                case ControlSource.NavigationSoftware:
                    return DentalControlSource.NavigationSoftware;
                case ControlSource.XrealGesture:
                    return DentalControlSource.XrealGesture;
                case ControlSource.BeamPro:
                    return DentalControlSource.BeamPro;
                default:
                    return DentalControlSource.Unspecified;
            }
        }

        static DentalRobotModelRenderer.DentalModelType ToRendererModelType(AssetType value)
        {
            switch (value)
            {
                case AssetType.StlTeeth:
                    return DentalRobotModelRenderer.DentalModelType.Teeth;
                case AssetType.StlDrill:
                    return DentalRobotModelRenderer.DentalModelType.Drill;
                default:
                    return DentalRobotModelRenderer.DentalModelType.Unknown;
            }
        }

        static ControlSource ToProto(DentalControlSource value)
        {
            switch (value)
            {
                case DentalControlSource.NavigationSoftware:
                    return ControlSource.NavigationSoftware;
                case DentalControlSource.XrealGesture:
                    return ControlSource.XrealGesture;
                case DentalControlSource.BeamPro:
                    return ControlSource.BeamPro;
                default:
                    return ControlSource.Unspecified;
            }
        }

        static Vector3 ToUnityVector(Vector3d value)
        {
            return value == null ? Vector3.zero : new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        static Vector3d ToProtoVector(Vector3 value)
        {
            return new Vector3d { X = value.x, Y = value.y, Z = value.z };
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
