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
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public partial class DentalRobotGrpcClient : MonoBehaviour
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
        static readonly string SessionInactivityTimeoutMessage = $"No data received from the surgical robot for {MessageInactivityTimeout.TotalSeconds:0} seconds.";
        static readonly string AssetInactivityTimeoutMessage = $"No data received for {MessageInactivityTimeout.TotalSeconds:0} seconds during asset transfer.";

        [SerializeField]
        bool m_ConnectOnStart = true;

        [SerializeField]
        bool m_AutoSearchInBackground = true;

        [SerializeField]
        float m_SearchIntervalSeconds = 5f;

        [SerializeField]
        int m_MaxMainThreadActionsPerFrame = 64;

        [SerializeField, Min(0.1f)]
        float m_MaxMainThreadDispatchMilliseconds = 2f;

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
        readonly DentalWifiLowLatencyLease m_WifiLatencyLease = new DentalWifiLowLatencyLease();
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
        bool m_ApplicationPaused;
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
        long m_LatestFrameReceivedTicks;
        long m_LastAcceptedFrameReceivedTicks;

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

        void OnApplicationPause(bool paused)
        {
            m_ApplicationPaused = paused;
            if (paused)
                m_WifiLatencyLease.Release();
            RefreshWifiLatencyLease();
        }

        void Update()
        {
            DrainMainThreadActions();
            RefreshWifiLatencyLease();

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

        void RefreshWifiLatencyLease()
        {
            // Background tasks only change managed connection state. JNI stays on Unity's
            // main thread, and the lease calls Android only when this desired state changes.
            m_WifiLatencyLease.SetRequired(isActiveAndEnabled && !m_ApplicationPaused
                && m_Cancellation != null && IsStreamRunning());
        }

        void DrainMainThreadActions()
        {
            var actionBudget = Mathf.Max(1, m_MaxMainThreadActionsPerFrame);
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var tickBudget = Math.Max(0.1f, m_MaxMainThreadDispatchMilliseconds)
                * System.Diagnostics.Stopwatch.Frequency / 1000.0;
            for (var i = 0; i < actionBudget && m_MainThreadActions.TryDequeue(out var work); i++)
            {
                work?.Invoke();
                // Yield between actions; an expensive asset callback must not be followed by
                // another full batch before Unity gets a chance to render or process input.
                if (System.Diagnostics.Stopwatch.GetTimestamp() - start >= tickBudget)
                    break;
            }
        }

        void OnDestroy()
        {
            UnbindObservationStreamer();
            Disconnect();
            m_WifiLatencyLease.Dispose();
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
            EnqueueLinkConnecting($"Searching for/connecting to surgical robot gRPC: {serverAddress}");
            m_StreamTask = Task.Run(() => RunConnectionAsync(
                serverAddress,
                deviceId,
                datasetId,
                m_ClientSessionId,
                cancellationToken,
                cancellation));
            RefreshWifiLatencyLease();
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
                EnqueueLinkConnecting($"Switching to gRPC server: {ServerAddress}");
                Disconnect();
                return;
            }

            m_ManualSearchPending = false;
            Connect();
        }

        public void Disconnect()
        {
            m_WifiLatencyLease.Release();
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
                using (var channel = DentalRobotGrpcChannelFactory.Create(serverAddress))
                {
                    var client = new DentalModelTransfer.DentalModelTransferClient(channel.CreateCallInvoker());
                    try
                    {
                        await RunV2ConnectionAsync(client, deviceId, datasetId, clientSessionId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented && !cancellationToken.IsCancellationRequested)
                    {
                        EnqueueStatus("The server does not support protocol v2; switched to v1 compatibility mode.");
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
                    EnqueueLinkLost("The surgical robot gRPC stream ended; retrying shortly.");
            }
            catch (OperationCanceledException)
            {
                if (!m_ServerSessionEnded)
                    EnqueueLinkLost("Disconnected from surgical robot gRPC.");
            }
            catch (Exception ex)
            {
                EnqueueLinkLost($"Surgical robot gRPC connection failed: {ex.GetType().Name}: {ex.Message}");
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
                    EnqueueStatus("The asset channel ended; preserving progress and reconnecting.");
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
                {
                    EnqueueStatus("The server does not provide a v2 asset channel; navigation continues.");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    EnqueueStatus($"The asset channel is unavailable; navigation continues while the transfer reconnects and resumes: {ex.Message}");
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
            using (var inactivityCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
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
                        if (RgbFeaturePolicy.Enabled)
                            request.RequestedCapabilities.Add(Capability.RgbView);
                    }

                    await writer.EnqueueAsync(new ClientMessage { Request = request }, cancellationToken).ConfigureAwait(false);
                    EnqueueStatus($"Sent {(isV2 ? "navigation session" : "model")} request device_id={deviceId}, dataset_id={datasetId}");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (!await MoveNextWithTimeoutAsync(
                                call.ResponseStream, inactivityCancellation, MessageInactivityTimeout,
                                SessionInactivityTimeoutMessage,
                                cancellationToken).ConfigureAwait(false))
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
                    await StopAndDisposeWriterAsync(writer, call.RequestStream).ConfigureAwait(false);
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
            using (var inactivityCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
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
                        if (m_AssetTransferInProgress)
                        {
                            if (!await MoveNextWithTimeoutAsync(
                                    call.ResponseStream, inactivityCancellation, MessageInactivityTimeout,
                                    AssetInactivityTimeoutMessage,
                                    cancellationToken).ConfigureAwait(false))
                                break;
                        }
                        else if (!await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
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
                    await StopAndDisposeWriterAsync(writer, call.RequestStream).ConfigureAwait(false);
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
                        "Threshold units must be mm and °");
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
            var rejectionMessage = "observation control rejected";
            var accepted = await EnqueueMainThreadAsync(() =>
            {
                var streamer = XrRgbRtpStreamer.Instance;
                if (control.RgbEnabled && !RgbFeaturePolicy.Enabled)
                {
                    rejectionMessage = RgbFeaturePolicy.DisabledMessage;
                    QueueObservationStatus(
                        control.SessionId,
                        control.ContextVersion,
                        control.ControlVersion,
                        streamer != null && streamer.IsStreaming && streamer.IncludeXr && streamer.HasLiveXrFrame,
                        streamer != null && streamer.IsStreaming && streamer.IncludeRgb && streamer.HasLiveRgbFrame,
                        rejectionMessage);
                    return false;
                }

                var state = DentalNavigationState.EnsureInstance();
                if (!state.CanApplyObservationControl(control))
                    return false;

                if (streamer == null)
                {
                    var disabled = !control.MirrorEnabled && !control.RgbEnabled;
                    if (disabled)
                        state.ApplyObservationControl(control);
                    else
                        rejectionMessage = "XR/RGB streamer is not ready";
                    QueueObservationStatus(
                        control.SessionId,
                        control.ContextVersion,
                        control.ControlVersion,
                        false,
                        false,
                        disabled ? string.Empty : rejectionMessage);
                    return disabled;
                }

                var executionOk = streamer.TryApplyControl(control, out var executionError);
                if (!executionOk && !string.IsNullOrEmpty(executionError))
                    rejectionMessage = executionError;
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
                accepted ? "observation control applied" : rejectionMessage,
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
                    state.NotifyNavigationStopped(string.IsNullOrEmpty(message.Reason) ? "Navigation paused" : message.Reason);
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
                ? (message.ReconnectAllowed ? "The asset channel requested reconnection." : "The asset channel ended.")
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
                EnqueueStatus("Asset manifest rejected: " + manifestError);
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
                    {
                        DentalPerformanceDiagnostics.RecordNavigationRejected();
                        return;
                    }
                    if (frame.ContextVersion == m_LatestFrameContextVersion && frame.Sequence <= m_LatestFrameSequence)
                    {
                        DentalPerformanceDiagnostics.RecordNavigationRejected();
                        return;
                    }
                }

                var receiveTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                var receiveUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var sameContext = m_HasLatestFrameSequence && sameSession
                    && frame.ContextVersion == m_LatestFrameContextVersion;
                var hasReceiveGap = sameContext && m_LastAcceptedFrameReceivedTicks > 0
                    && receiveTicks >= m_LastAcceptedFrameReceivedTicks;
                var receiveGapMs = hasReceiveGap
                    ? (float)((receiveTicks - m_LastAcceptedFrameReceivedTicks) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency)
                    : 0f;
                var skippedSequences = sameContext && frame.Sequence - m_LatestFrameSequence > 1
                    ? frame.Sequence - m_LatestFrameSequence - 1
                    : 0;
                const long earliestSupportedUnixMs = 946684800000L; // 2000-01-01 UTC
                var sourceTimeValid = frame.CaptureTimeUnixMs >= earliestSupportedUnixMs
                    && frame.CaptureTimeUnixMs <= receiveUnixMs + 5000L;
                var sourceAgeMs = sourceTimeValid
                    ? (float)((double)receiveUnixMs - frame.CaptureTimeUnixMs)
                    : 0f;
                var overwrotePending = m_HasLatestFrame;
                m_LatestFrame = frame.Clone();
                m_HasLatestFrame = true;
                m_HasLatestFrameSequence = true;
                m_LatestFrameSessionId = frame.SessionId ?? string.Empty;
                m_LatestFrameContextVersion = frame.ContextVersion;
                m_LatestFrameSequence = frame.Sequence;
                m_LatestFrameReceivedTicks = receiveTicks;
                m_LastAcceptedFrameReceivedTicks = receiveTicks;
                DentalPerformanceDiagnostics.RecordNavigationReceived(
                    receiveGapMs, hasReceiveGap, sourceAgeMs, sourceTimeValid,
                    skippedSequences, overwrotePending);
            }
        }

        void ApplyLatestNavigationFrame()
        {
            NavigationFrame frame;
            long receiveTicks;
            lock (m_LatestFrameLock)
            {
                if (!m_HasLatestFrame)
                    return;

                frame = m_LatestFrame;
                receiveTicks = m_LatestFrameReceivedTicks;
                m_LatestFrame = null;
                m_HasLatestFrame = false;
            }

            var unitsValid = frame.DistanceUnit == DistanceUnit.Millimeter && frame.AngleUnit == AngleUnit.Degree;
            var data = ToDomain(frame, unitsValid);
            var accepted = DentalNavigationState.EnsureInstance().ApplyNavigationFrame(data);
            var applyTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            var receiveToApplyMs = receiveTicks > 0 && applyTicks >= receiveTicks
                ? (float)((applyTicks - receiveTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency)
                : 0f;
            DentalPerformanceDiagnostics.RecordNavigationApplied(receiveToApplyMs, accepted);
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
                m_LatestFrameReceivedTicks = 0;
                m_LastAcceptedFrameReceivedTicks = 0;
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

    }
}
