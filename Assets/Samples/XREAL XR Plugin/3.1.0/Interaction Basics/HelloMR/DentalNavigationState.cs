using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public enum DentalLinkState
    {
        Idle = 0,
        Connecting = 1,
        Live = 2,
        Lost = 3,
    }

    public enum DentalControlSource
    {
        Unspecified = 0,
        NavigationSoftware = 1,
        XrealGesture = 2,
        BeamPro = 3,
    }

    public enum DentalThresholdBoundaryRule
    {
        Unspecified = 0,
        UpperBoundsInclusive = 1,
        UpperBoundsExclusive = 2,
    }

    public readonly struct DentalNavigationContext
    {
        public readonly string SessionId;
        public readonly string CaseId;
        public readonly string DatasetId;
        public readonly string CtId;
        public readonly string PlanId;
        public readonly string ToothId;
        public readonly string ToolId;
        public readonly string StepId;
        public readonly ulong ContextVersion;
        public readonly Vector3 PlanEntryMm;
        public readonly Vector3 PlanAxis;
        public readonly float TargetDepthMm;
        public readonly Vector3 BuccalAxis;
        public readonly Vector3 MesialAxis;
        public readonly string CoordinateFrameId;
        public readonly bool HasPatientFromDicom;
        public readonly Matrix4x4 PatientFromDicom;

        public DentalNavigationContext(
            string sessionId,
            string caseId,
            string datasetId,
            string ctId,
            string planId,
            string toothId,
            string toolId,
            string stepId,
            ulong contextVersion,
            Vector3 planEntryMm,
            Vector3 planAxis,
            float targetDepthMm,
            Vector3 buccalAxis,
            Vector3 mesialAxis,
            string coordinateFrameId,
            bool hasPatientFromDicom,
            Matrix4x4 patientFromDicom)
        {
            SessionId = sessionId ?? string.Empty;
            CaseId = caseId ?? string.Empty;
            DatasetId = datasetId ?? string.Empty;
            CtId = ctId ?? string.Empty;
            PlanId = planId ?? string.Empty;
            ToothId = toothId ?? string.Empty;
            ToolId = toolId ?? string.Empty;
            StepId = stepId ?? string.Empty;
            ContextVersion = contextVersion;
            PlanEntryMm = planEntryMm;
            PlanAxis = planAxis;
            TargetDepthMm = targetDepthMm;
            BuccalAxis = buccalAxis;
            MesialAxis = mesialAxis;
            CoordinateFrameId = coordinateFrameId ?? string.Empty;
            HasPatientFromDicom = hasPatientFromDicom;
            PatientFromDicom = patientFromDicom;
        }
    }

    public readonly struct DentalNavigationThresholds
    {
        public readonly string SessionId;
        public readonly ulong ContextVersion;
        public readonly ulong ConfigVersion;
        public readonly float LateralGreenMaxMm;
        public readonly float LateralRedMinMm;
        public readonly float LateralHysteresisMm;
        public readonly float AngleGreenMaxDeg;
        public readonly float AngleRedMinDeg;
        public readonly float AngleHysteresisDeg;
        public readonly float DepthApproachMm;
        public readonly float DepthAtTargetToleranceMm;
        public readonly float DepthOverrunRedMm;
        public readonly float DepthHysteresisMm;
        public readonly DentalThresholdBoundaryRule BoundaryRule;

        public DentalNavigationThresholds(
            string sessionId,
            ulong contextVersion,
            ulong configVersion,
            float lateralGreenMaxMm,
            float lateralRedMinMm,
            float lateralHysteresisMm,
            float angleGreenMaxDeg,
            float angleRedMinDeg,
            float angleHysteresisDeg,
            float depthApproachMm,
            float depthAtTargetToleranceMm,
            float depthOverrunRedMm,
            float depthHysteresisMm,
            DentalThresholdBoundaryRule boundaryRule)
        {
            SessionId = sessionId ?? string.Empty;
            ContextVersion = contextVersion;
            ConfigVersion = configVersion;
            LateralGreenMaxMm = lateralGreenMaxMm;
            LateralRedMinMm = lateralRedMinMm;
            LateralHysteresisMm = lateralHysteresisMm;
            AngleGreenMaxDeg = angleGreenMaxDeg;
            AngleRedMinDeg = angleRedMinDeg;
            AngleHysteresisDeg = angleHysteresisDeg;
            DepthApproachMm = depthApproachMm;
            DepthAtTargetToleranceMm = depthAtTargetToleranceMm;
            DepthOverrunRedMm = depthOverrunRedMm;
            DepthHysteresisMm = depthHysteresisMm;
            BoundaryRule = boundaryRule;
        }

        public bool IsValid =>
            ContextVersion > 0 &&
            ConfigVersion > 0 &&
            IsFiniteNonNegative(LateralGreenMaxMm) &&
            IsFiniteNonNegative(LateralRedMinMm) &&
            LateralRedMinMm >= LateralGreenMaxMm &&
            IsFiniteNonNegative(LateralHysteresisMm) &&
            LateralHysteresisMm <= LateralGreenMaxMm &&
            LateralHysteresisMm <= LateralRedMinMm - LateralGreenMaxMm &&
            IsFiniteNonNegative(AngleGreenMaxDeg) &&
            IsFiniteNonNegative(AngleRedMinDeg) &&
            AngleRedMinDeg >= AngleGreenMaxDeg &&
            IsFiniteNonNegative(AngleHysteresisDeg) &&
            AngleHysteresisDeg <= AngleGreenMaxDeg &&
            AngleHysteresisDeg <= AngleRedMinDeg - AngleGreenMaxDeg &&
            IsFiniteNonNegative(DepthApproachMm) &&
            IsFiniteNonNegative(DepthAtTargetToleranceMm) &&
            IsFiniteNonNegative(DepthOverrunRedMm) &&
            IsFiniteNonNegative(DepthHysteresisMm) &&
            DepthHysteresisMm <= DepthApproachMm &&
            DepthHysteresisMm <= DepthOverrunRedMm &&
            BoundaryRule != DentalThresholdBoundaryRule.Unspecified;

        static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }

    public readonly struct DentalNavigationFrameData
    {
        public readonly string SessionId;
        public readonly ulong ContextVersion;
        public readonly ulong Sequence;
        public readonly long CaptureTimeUnixMs;
        public readonly bool Valid;
        public readonly string InvalidReason;
        public readonly Vector3 DrillTipMm;
        public readonly Vector3 DrillAxis;
        public readonly bool HasDrillMatrix;
        public readonly Matrix4x4 DrillFromTeeth;
        public readonly float LateralMm;
        public readonly float LateralBuccalMm;
        public readonly float LateralMesialMm;
        public readonly float AngleDeg;
        public readonly float TiltBuccalDeg;
        public readonly float TiltMesialDeg;
        public readonly float CurrentDepthMm;
        public readonly float TargetDepthMm;
        public readonly float RemainingDepthMm;
        public readonly bool HasLateralDirection;
        public readonly bool HasTiltDirection;
        public readonly bool HasDepthBreakdown;

        public DentalNavigationFrameData(
            string sessionId,
            ulong contextVersion,
            ulong sequence,
            long captureTimeUnixMs,
            bool valid,
            string invalidReason,
            Vector3 drillTipMm,
            Vector3 drillAxis,
            bool hasDrillMatrix,
            Matrix4x4 drillFromTeeth,
            float lateralMm,
            float lateralBuccalMm,
            float lateralMesialMm,
            float angleDeg,
            float tiltBuccalDeg,
            float tiltMesialDeg,
            float currentDepthMm,
            float targetDepthMm,
            float remainingDepthMm,
            bool hasLateralDirection,
            bool hasTiltDirection,
            bool hasDepthBreakdown)
        {
            SessionId = sessionId ?? string.Empty;
            ContextVersion = contextVersion;
            Sequence = sequence;
            CaptureTimeUnixMs = captureTimeUnixMs;
            Valid = valid;
            InvalidReason = invalidReason ?? string.Empty;
            DrillTipMm = drillTipMm;
            DrillAxis = drillAxis;
            HasDrillMatrix = hasDrillMatrix;
            DrillFromTeeth = drillFromTeeth;
            LateralMm = lateralMm;
            LateralBuccalMm = lateralBuccalMm;
            LateralMesialMm = lateralMesialMm;
            AngleDeg = angleDeg;
            TiltBuccalDeg = tiltBuccalDeg;
            TiltMesialDeg = tiltMesialDeg;
            CurrentDepthMm = currentDepthMm;
            TargetDepthMm = targetDepthMm;
            RemainingDepthMm = remainingDepthMm;
            HasLateralDirection = hasLateralDirection;
            HasTiltDirection = hasTiltDirection;
            HasDepthBreakdown = hasDepthBreakdown;
        }
    }

    public readonly struct DentalSliceState
    {
        public readonly string SessionId;
        public readonly ulong ContextVersion;
        public readonly ulong ControlVersion;
        public readonly bool SyncEnabled;
        public readonly DentalControlSource Source;
        public readonly bool HasPlane;
        public readonly string VolumeId;
        public readonly string FrameOfReferenceUid;
        public readonly Vector3 PlaneOriginMm;
        public readonly Vector3 PlaneNormal;
        public readonly Vector3 PlaneUp;
        public readonly float OffsetMm;
        public readonly int SliceIndex;
        public readonly string SopInstanceUid;

        public DentalSliceState(
            string sessionId,
            ulong contextVersion,
            ulong controlVersion,
            bool syncEnabled,
            DentalControlSource source,
            bool hasPlane,
            string volumeId,
            string frameOfReferenceUid,
            Vector3 planeOriginMm,
            Vector3 planeNormal,
            Vector3 planeUp,
            float offsetMm,
            int sliceIndex,
            string sopInstanceUid)
        {
            SessionId = sessionId ?? string.Empty;
            ContextVersion = contextVersion;
            ControlVersion = controlVersion;
            SyncEnabled = syncEnabled;
            Source = source;
            HasPlane = hasPlane;
            VolumeId = volumeId ?? string.Empty;
            FrameOfReferenceUid = frameOfReferenceUid ?? string.Empty;
            PlaneOriginMm = planeOriginMm;
            PlaneNormal = planeNormal;
            PlaneUp = planeUp;
            OffsetMm = offsetMm;
            SliceIndex = sliceIndex;
            SopInstanceUid = sopInstanceUid ?? string.Empty;
        }
    }

    public readonly struct DentalDisplayLayoutState
    {
        public readonly string SessionId;
        public readonly ulong ContextVersion;
        public readonly ulong ControlVersion;
        public readonly DentalControlSource Source;
        public readonly bool HudVisible;
        public readonly bool ModelVisible;
        public readonly Vector3 HudPositionMeters;
        public readonly Vector3 ModelPositionMeters;
        public readonly bool ResetToDefault;

        public DentalDisplayLayoutState(
            string sessionId,
            ulong contextVersion,
            ulong controlVersion,
            DentalControlSource source,
            bool hudVisible,
            bool modelVisible,
            Vector3 hudPositionMeters,
            Vector3 modelPositionMeters,
            bool resetToDefault)
        {
            SessionId = sessionId ?? string.Empty;
            ContextVersion = contextVersion;
            ControlVersion = controlVersion;
            Source = source;
            HudVisible = hudVisible;
            ModelVisible = modelVisible;
            HudPositionMeters = hudPositionMeters;
            ModelPositionMeters = modelPositionMeters;
            ResetToDefault = resetToDefault;
        }
    }

    public readonly struct DentalObservationControlState
    {
        public readonly string SessionId;
        public readonly ulong ContextVersion;
        public readonly ulong ControlVersion;
        public readonly bool MirrorEnabled;
        public readonly bool RgbEnabled;
        public readonly string ReceiverHost;
        public readonly int ReceiverPort;
        public readonly int Width;
        public readonly int Height;
        public readonly int Fps;

        public DentalObservationControlState(
            string sessionId,
            ulong contextVersion,
            ulong controlVersion,
            bool mirrorEnabled,
            bool rgbEnabled,
            string receiverHost,
            int receiverPort,
            int width,
            int height,
            int fps)
        {
            SessionId = sessionId ?? string.Empty;
            ContextVersion = contextVersion;
            ControlVersion = controlVersion;
            MirrorEnabled = mirrorEnabled;
            RgbEnabled = rgbEnabled;
            ReceiverHost = receiverHost ?? string.Empty;
            ReceiverPort = receiverPort;
            Width = width;
            Height = height;
            Fps = fps;
        }
    }

    public readonly struct DentalNavigationSnapshot
    {
        public readonly bool HasMetadata;
        public readonly string DatasetId;
        public readonly float DepthMm;
        public readonly float LateralMm;
        public readonly float AngleDeg;
        public readonly float AgeSeconds;
        public readonly DentalLinkState Link;
        public readonly string LinkMessage;
        public readonly bool HasDrillMatrix;
        public readonly Matrix4x4 DrillFromTeeth;
        public readonly float DistanceToMillimeters;
        public readonly float AngleToDegrees;

        public readonly bool HasContext;
        public readonly DentalNavigationContext Context;
        public readonly ulong ContextVersion;
        public readonly string SessionId;
        public readonly string CaseId;
        public readonly string CtId;
        public readonly string PlanId;
        public readonly string ToothId;
        public readonly string ToolId;
        public readonly string StepId;
        public readonly bool HasThresholds;
        public readonly DentalNavigationThresholds Thresholds;
        public readonly bool HasNavigationFrame;
        public readonly ulong FrameSequence;
        public readonly long CaptureTimeUnixMs;
        public readonly bool FrameValid;
        public readonly string InvalidReason;
        public readonly bool HasLateralDirection;
        public readonly float LateralBuccalMm;
        public readonly float LateralMesialMm;
        public readonly bool HasTiltDirection;
        public readonly float TiltBuccalDeg;
        public readonly float TiltMesialDeg;
        public readonly bool HasDepthBreakdown;
        public readonly float CurrentDepthMm;
        public readonly float TargetDepthMm;
        public readonly float RemainingDepthMm;
        public readonly Vector3 DrillTipMm;
        public readonly Vector3 DrillAxis;
        public readonly bool HasSliceState;
        public readonly DentalSliceState SliceState;
        public readonly bool HasDisplayLayout;
        public readonly DentalDisplayLayoutState DisplayLayout;
        public readonly bool HasObservationControl;
        public readonly DentalObservationControlState ObservationControl;

        internal DentalNavigationSnapshot(
            bool hasMetadata,
            string datasetId,
            float depthMm,
            float lateralMm,
            float angleDeg,
            float ageSeconds,
            DentalLinkState link,
            string linkMessage,
            bool hasDrillMatrix,
            Matrix4x4 drillFromTeeth,
            float distanceToMillimeters,
            float angleToDegrees,
            bool hasContext,
            DentalNavigationContext context,
            bool hasThresholds,
            DentalNavigationThresholds thresholds,
            bool hasNavigationFrame,
            ulong frameSequence,
            long captureTimeUnixMs,
            bool frameValid,
            string invalidReason,
            bool hasLateralDirection,
            float lateralBuccalMm,
            float lateralMesialMm,
            bool hasTiltDirection,
            float tiltBuccalDeg,
            float tiltMesialDeg,
            bool hasDepthBreakdown,
            float currentDepthMm,
            float targetDepthMm,
            float remainingDepthMm,
            Vector3 drillTipMm,
            Vector3 drillAxis,
            bool hasSliceState,
            DentalSliceState sliceState,
            bool hasDisplayLayout,
            DentalDisplayLayoutState displayLayout,
            bool hasObservationControl,
            DentalObservationControlState observationControl)
        {
            HasMetadata = hasMetadata;
            DatasetId = datasetId;
            DepthMm = depthMm;
            LateralMm = lateralMm;
            AngleDeg = angleDeg;
            AgeSeconds = ageSeconds;
            Link = link;
            LinkMessage = linkMessage;
            HasDrillMatrix = hasDrillMatrix;
            DrillFromTeeth = drillFromTeeth;
            DistanceToMillimeters = distanceToMillimeters;
            AngleToDegrees = angleToDegrees;
            HasContext = hasContext;
            Context = context;
            ContextVersion = hasContext ? context.ContextVersion : 0;
            SessionId = hasContext ? context.SessionId : string.Empty;
            CaseId = hasContext ? context.CaseId : string.Empty;
            CtId = hasContext ? context.CtId : string.Empty;
            PlanId = hasContext ? context.PlanId : string.Empty;
            ToothId = hasContext ? context.ToothId : string.Empty;
            ToolId = hasContext ? context.ToolId : string.Empty;
            StepId = hasContext ? context.StepId : string.Empty;
            HasThresholds = hasThresholds;
            Thresholds = thresholds;
            HasNavigationFrame = hasNavigationFrame;
            FrameSequence = frameSequence;
            CaptureTimeUnixMs = captureTimeUnixMs;
            FrameValid = frameValid;
            InvalidReason = invalidReason;
            HasLateralDirection = hasLateralDirection;
            LateralBuccalMm = lateralBuccalMm;
            LateralMesialMm = lateralMesialMm;
            HasTiltDirection = hasTiltDirection;
            TiltBuccalDeg = tiltBuccalDeg;
            TiltMesialDeg = tiltMesialDeg;
            HasDepthBreakdown = hasDepthBreakdown;
            CurrentDepthMm = currentDepthMm;
            TargetDepthMm = targetDepthMm;
            RemainingDepthMm = remainingDepthMm;
            DrillTipMm = drillTipMm;
            DrillAxis = drillAxis;
            HasSliceState = hasSliceState;
            SliceState = sliceState;
            HasDisplayLayout = hasDisplayLayout;
            DisplayLayout = displayLayout;
            HasObservationControl = hasObservationControl;
            ObservationControl = observationControl;
        }

        public bool IsFresh => HasMetadata && FrameValid && Link == DentalLinkState.Live && AgeSeconds <= 0.20f;
        public bool IsAging => HasMetadata && FrameValid && Link == DentalLinkState.Live && AgeSeconds > 0.20f && AgeSeconds <= 0.50f;
        public bool IsStale => HasMetadata && FrameValid && AgeSeconds > 0.50f;
        public bool HideNumbers => Link == DentalLinkState.Lost || Link == DentalLinkState.Idle || !HasMetadata || !FrameValid || AgeSeconds > 0.50f;
    }

    public sealed class DentalNavigationState : MonoBehaviour
    {
        public const string LogSource = "手术机器人";

        [SerializeField]
        float m_DistanceToMillimeters = 1000f;

        [SerializeField]
        float m_AngleToDegrees = 1f;

        static DentalNavigationState s_Instance;

        bool m_HasMetadata;
        string m_DatasetId = string.Empty;
        double m_Distance;
        double m_LateralDistance;
        double m_Angle;
        float m_LastMetadataRealtime = -1f;
        float m_SourceAgeSecondsAtArrival;
        DentalLinkState m_Link = DentalLinkState.Idle;
        string m_LinkMessage = string.Empty;
        bool m_HasDrillMatrix;
        Matrix4x4 m_DrillFromTeeth = Matrix4x4.identity;
        DentalHudEvaluation m_LastEvaluation;
        bool m_LoggedFirstMetadata;

        bool m_HasContext;
        DentalNavigationContext m_Context;
        bool m_HasThresholds;
        DentalNavigationThresholds m_Thresholds;
        bool m_HasThresholdConfigVersion;
        ulong m_LastThresholdConfigVersion;
        bool m_HasNavigationFrame;
        bool m_HasAcceptedFrameSequence;
        ulong m_FrameSequence;
        long m_CaptureTimeUnixMs;
        bool m_FrameValid;
        string m_InvalidReason = string.Empty;
        bool m_HasLateralDirection;
        double m_LateralBuccalMm;
        double m_LateralMesialMm;
        bool m_HasTiltDirection;
        double m_TiltBuccalDeg;
        double m_TiltMesialDeg;
        bool m_HasDepthBreakdown;
        double m_CurrentDepthMm;
        double m_TargetDepthMm;
        double m_RemainingDepthMm;
        Vector3 m_DrillTipMm;
        Vector3 m_DrillAxis;
        bool m_HasSliceState;
        DentalSliceState m_SliceState;
        bool m_HasDisplayLayout;
        DentalDisplayLayoutState m_DisplayLayout;
        bool m_HasObservationControl;
        DentalObservationControlState m_ObservationControl;

        public static DentalNavigationState Instance => s_Instance;
        public DentalHudEvaluation LastEvaluation => m_LastEvaluation;

        public event Action<DentalNavigationSnapshot> Changed;
        public event Action<DentalNavigationContext> ContextChanged;
        public event Action<DentalNavigationThresholds> ThresholdsChanged;
        public event Action<DentalSliceState> SliceStateChanged;
        public event Action<DentalDisplayLayoutState> DisplayLayoutChanged;
        public event Action<DentalObservationControlState> ObservationControlChanged;

        public static DentalNavigationState EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = FindObjectOfType<DentalNavigationState>();
            if (existing != null)
            {
                s_Instance = existing;
                return s_Instance;
            }

            var obj = new GameObject("Dental Navigation State");
            s_Instance = obj.AddComponent<DentalNavigationState>();
            return s_Instance;
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void SetUnitConversion(float distanceToMillimeters, float angleToDegrees)
        {
            if (distanceToMillimeters > 0f)
                m_DistanceToMillimeters = distanceToMillimeters;

            if (angleToDegrees > 0f)
                m_AngleToDegrees = angleToDegrees;
        }

        public void NotifyConnecting(string endpoint)
        {
            ClearDynamicNavigation("连接中");
            m_Link = DentalLinkState.Connecting;
            m_LinkMessage = string.IsNullOrEmpty(endpoint) ? "连接中" : endpoint;
            RaiseChanged();
        }

        public void NotifyDisconnected(string reason)
        {
            ClearDynamicNavigation(reason);
            m_Link = DentalLinkState.Lost;
            m_LinkMessage = string.IsNullOrEmpty(reason) ? "未连接机器人" : reason;
            RaiseChanged();
        }

        public bool ApplyNavigationContext(DentalNavigationContext context)
        {
            if (string.IsNullOrEmpty(context.SessionId) || context.ContextVersion == 0)
                return false;

            if (!ContextValuesAreValid(context))
                return false;

            if (m_HasContext && string.Equals(m_Context.SessionId, context.SessionId, StringComparison.Ordinal))
            {
                if (context.ContextVersion < m_Context.ContextVersion)
                    return false;

                if (context.ContextVersion == m_Context.ContextVersion)
                    return ContextIdentityEquals(m_Context, context);
            }

            var sessionChanged = m_HasContext && !string.Equals(m_Context.SessionId, context.SessionId, StringComparison.Ordinal);
            var versionChanged = !m_HasContext || context.ContextVersion != m_Context.ContextVersion;
            m_Context = context;
            m_HasContext = true;
            m_DatasetId = context.DatasetId;
            m_TargetDepthMm = context.TargetDepthMm;
            m_DistanceToMillimeters = 1f;
            m_AngleToDegrees = 1f;
            m_Link = DentalLinkState.Live;
            m_LinkMessage = "导航上下文已同步";

            if (sessionChanged || versionChanged)
            {
                ClearDynamicNavigation("等待导航数据");
                m_HasThresholds = false;
                m_HasThresholdConfigVersion = false;
                m_LastThresholdConfigVersion = 0;
                m_HasAcceptedFrameSequence = false;
                m_FrameSequence = 0;
                m_HasSliceState = false;
                m_HasDisplayLayout = false;
                m_HasObservationControl = false;
            }

            ContextChanged?.Invoke(context);
            RaiseChanged();
            return true;
        }

        public bool ApplyThresholds(DentalNavigationThresholds thresholds)
        {
            if (!MatchesActiveContext(thresholds.SessionId, thresholds.ContextVersion))
                return false;

            if (m_HasThresholdConfigVersion)
            {
                if (thresholds.ConfigVersion < m_LastThresholdConfigVersion)
                    return false;
                if (thresholds.ConfigVersion == m_LastThresholdConfigVersion)
                    return m_HasThresholds && ThresholdsEqual(m_Thresholds, thresholds);
            }

            m_HasThresholdConfigVersion = true;
            m_LastThresholdConfigVersion = thresholds.ConfigVersion;

            if (!thresholds.IsValid)
            {
                m_HasThresholds = false;
                m_Thresholds = thresholds;
                m_LinkMessage = "阈值配置无效";
                RaiseChanged();
                return false;
            }

            m_Thresholds = thresholds;
            m_HasThresholds = true;
            ThresholdsChanged?.Invoke(thresholds);
            RaiseChanged();
            return true;
        }

        public void NotifyThresholdsUnavailable(
            string sessionId,
            ulong contextVersion,
            ulong configVersion,
            string reason)
        {
            if (!MatchesActiveContext(sessionId, contextVersion))
                return;

            if (m_HasThresholdConfigVersion && configVersion < m_LastThresholdConfigVersion)
                return;

            m_HasThresholdConfigVersion = true;
            m_LastThresholdConfigVersion = configVersion;
            m_HasThresholds = false;
            m_LinkMessage = string.IsNullOrEmpty(reason) ? "阈值未同步" : reason;
            RaiseChanged();
        }

        public bool ApplyNavigationFrame(DentalNavigationFrameData frame)
        {
            if (!MatchesActiveContext(frame.SessionId, frame.ContextVersion))
                return false;

            if (m_HasAcceptedFrameSequence && frame.Sequence <= m_FrameSequence)
                return false;

            m_HasAcceptedFrameSequence = true;
            m_FrameSequence = frame.Sequence;
            m_CaptureTimeUnixMs = frame.CaptureTimeUnixMs;
            m_LastMetadataRealtime = Time.realtimeSinceStartup;
            var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            const long earliestSupportedUnixMs = 946684800000L; // 2000-01-01 UTC
            var timestampValid = frame.CaptureTimeUnixMs >= earliestSupportedUnixMs
                && frame.CaptureTimeUnixMs <= nowUnixMs + 5000L;
            m_SourceAgeSecondsAtArrival = timestampValid
                ? Mathf.Max(0f, (float)(((double)nowUnixMs - frame.CaptureTimeUnixMs) / 1000.0))
                : 0f;
            var targetDepthMatches = !frame.HasDepthBreakdown
                || Mathf.Abs(frame.TargetDepthMm - m_Context.TargetDepthMm) <= 0.05f;
            m_FrameValid = frame.Valid
                && timestampValid
                && targetDepthMatches
                && FrameValuesAreFinite(frame);
            m_InvalidReason = m_FrameValid
                ? string.Empty
                : (!timestampValid
                    ? "导航帧采集时间无效"
                    : (!targetDepthMatches
                        ? "导航帧目标深度与当前步骤不一致"
                        : (string.IsNullOrEmpty(frame.InvalidReason) ? "导航数据无效" : frame.InvalidReason)));
            // An invalid frame is still a received v2 frame. Preserve its
            // sequence and reason while marking all dynamic values unusable.
            m_HasNavigationFrame = true;
            m_HasMetadata = true;
            m_Link = DentalLinkState.Live;
            m_LinkMessage = m_FrameValid ? "已连接" : m_InvalidReason;

            if (m_FrameValid)
            {
                m_Distance = frame.RemainingDepthMm;
                m_LateralDistance = Math.Abs(frame.LateralMm);
                m_Angle = Math.Abs(frame.AngleDeg);
                m_LateralBuccalMm = frame.LateralBuccalMm;
                m_LateralMesialMm = frame.LateralMesialMm;
                m_TiltBuccalDeg = frame.TiltBuccalDeg;
                m_TiltMesialDeg = frame.TiltMesialDeg;
                m_CurrentDepthMm = frame.CurrentDepthMm;
                m_TargetDepthMm = frame.TargetDepthMm;
                m_RemainingDepthMm = frame.RemainingDepthMm;
                m_HasLateralDirection = frame.HasLateralDirection;
                m_HasTiltDirection = frame.HasTiltDirection;
                m_HasDepthBreakdown = frame.HasDepthBreakdown;
                m_DrillTipMm = frame.DrillTipMm;
                m_DrillAxis = frame.DrillAxis;
                m_HasDrillMatrix = frame.HasDrillMatrix;
                m_DrillFromTeeth = frame.DrillFromTeeth;
            }
            else
            {
                ClearFrameValues();
            }

            RaiseChanged();
            return true;
        }

        public bool ApplySliceState(DentalSliceState sliceState)
        {
            if (!CanApplySliceState(sliceState))
                return false;

            if (m_HasSliceState && sliceState.ControlVersion == m_SliceState.ControlVersion)
                return true;

            m_SliceState = sliceState;
            m_HasSliceState = true;
            SliceStateChanged?.Invoke(sliceState);
            RaiseChanged();
            return true;
        }

        public bool CanApplySliceState(DentalSliceState sliceState)
        {
            if (sliceState.ControlVersion == 0
                || sliceState.Source == DentalControlSource.Unspecified
                || !MatchesActiveContext(sliceState.SessionId, sliceState.ContextVersion)
                || !SliceValuesAreValid(sliceState))
                return false;

            if (!m_HasSliceState)
                return true;
            if (sliceState.ControlVersion < m_SliceState.ControlVersion)
                return false;
            return sliceState.ControlVersion != m_SliceState.ControlVersion
                || SliceStatesEqual(m_SliceState, sliceState);
        }

        public bool ApplyDisplayLayout(DentalDisplayLayoutState displayLayout)
        {
            if (displayLayout.ControlVersion == 0
                || displayLayout.Source == DentalControlSource.Unspecified
                || !MatchesActiveContext(displayLayout.SessionId, displayLayout.ContextVersion))
                return false;
            if (!displayLayout.ResetToDefault
                && (!IsFinite(displayLayout.HudPositionMeters)
                    || !IsFinite(displayLayout.ModelPositionMeters)))
                return false;

            if (m_HasDisplayLayout)
            {
                if (displayLayout.ControlVersion < m_DisplayLayout.ControlVersion)
                    return false;
                if (displayLayout.ControlVersion == m_DisplayLayout.ControlVersion)
                    return DisplayLayoutsEqual(m_DisplayLayout, displayLayout);
            }

            m_DisplayLayout = displayLayout;
            m_HasDisplayLayout = true;
            DisplayLayoutChanged?.Invoke(displayLayout);
            RaiseChanged();
            return true;
        }

        public bool ApplyObservationControl(DentalObservationControlState observationControl)
        {
            if (!CanApplyObservationControl(observationControl))
                return false;

            m_ObservationControl = observationControl;
            m_HasObservationControl = true;
            ObservationControlChanged?.Invoke(observationControl);
            RaiseChanged();
            return true;
        }

        public bool CanApplyObservationControl(DentalObservationControlState observationControl)
        {
            if (observationControl.ControlVersion == 0
                || !MatchesActiveContext(observationControl.SessionId, observationControl.ContextVersion))
                return false;
            if (!m_HasObservationControl)
                return true;
            if (observationControl.ControlVersion < m_ObservationControl.ControlVersion)
                return false;
            return observationControl.ControlVersion != m_ObservationControl.ControlVersion
                || ObservationControlsEqual(m_ObservationControl, observationControl);
        }

        public void NotifyNavigationStopped(string reason)
        {
            ClearDynamicNavigation(reason);
            m_Link = DentalLinkState.Live;
            m_LinkMessage = string.IsNullOrEmpty(reason) ? "导航已停止" : reason;
            RaiseChanged();
        }

        public void NotifyNavigationActive(string message)
        {
            m_Link = DentalLinkState.Live;
            m_LinkMessage = string.IsNullOrEmpty(message) ? "导航已启动" : message;
            RaiseChanged();
        }

        public void NotifySessionEnded(string reason)
        {
            ClearDynamicNavigation(reason);
            m_Link = DentalLinkState.Lost;
            m_LinkMessage = string.IsNullOrEmpty(reason) ? "导航会话已结束" : reason;
            RaiseChanged();
        }

        // V1 compatibility path. Distance is interpreted as remaining depth,
        // because that is how the original HUD consumed this field.
        public void ApplyMetadata(
            string datasetId,
            IList<double> drillFromTeeth,
            double distance,
            double lateralDistance,
            double angle)
        {
            m_HasMetadata = true;
            m_HasNavigationFrame = true;
            m_FrameValid = true;
            m_HasAcceptedFrameSequence = true;
            m_FrameSequence++;
            m_DatasetId = string.IsNullOrEmpty(datasetId) ? string.Empty : datasetId;
            m_Distance = distance;
            m_LateralDistance = lateralDistance;
            m_Angle = angle;
            m_RemainingDepthMm = distance * m_DistanceToMillimeters;
            m_LastMetadataRealtime = Time.realtimeSinceStartup;
            m_SourceAgeSecondsAtArrival = 0f;
            m_Link = DentalLinkState.Live;
            m_LinkMessage = "已连接（兼容模式）";
            m_HasDrillMatrix = drillFromTeeth != null && drillFromTeeth.Count >= 16;
            m_DrillFromTeeth = ToUnityMatrix(drillFromTeeth);
            m_HasLateralDirection = false;
            m_HasTiltDirection = false;
            m_HasDepthBreakdown = false;
            m_CaptureTimeUnixMs = 0;
            m_InvalidReason = string.Empty;

            if (!m_LoggedFirstMetadata)
            {
                m_LoggedFirstMetadata = true;
                Debug.Log(
                    $"DentalNavigationState: first metadata dataset={m_DatasetId}, " +
                    $"remainingDepthMm={m_Distance * m_DistanceToMillimeters:0.###}, " +
                    $"lateralMm={Math.Abs(m_LateralDistance) * m_DistanceToMillimeters:0.###}, " +
                    $"angleDeg={Math.Abs(m_Angle) * m_AngleToDegrees:0.###}");
            }

            RaiseChanged();
        }

        public void NotifyModelTransfer(bool ok, string message)
        {
            if (!ok)
                m_LinkMessage = string.IsNullOrEmpty(message) ? "模型传输失败" : message;

            RaiseChanged();
        }

        public void SetEvaluation(DentalHudEvaluation evaluation)
        {
            m_LastEvaluation = evaluation;
        }

        public DentalNavigationSnapshot Capture(float nowRealtime)
        {
            var age = m_HasNavigationFrame && m_LastMetadataRealtime >= 0f
                ? m_SourceAgeSecondsAtArrival + Mathf.Max(0f, nowRealtime - m_LastMetadataRealtime)
                : float.PositiveInfinity;
            var distanceScale = m_HasContext ? 1f : m_DistanceToMillimeters;
            var angleScale = m_HasContext ? 1f : m_AngleToDegrees;
            var remainingDepthMm = m_HasContext
                ? (float)m_RemainingDepthMm
                : (float)(m_Distance * distanceScale);

            return new DentalNavigationSnapshot(
                m_HasMetadata,
                string.IsNullOrEmpty(m_DatasetId) ? "—" : m_DatasetId,
                remainingDepthMm,
                (float)(Math.Abs(m_LateralDistance) * distanceScale),
                (float)(Math.Abs(m_Angle) * angleScale),
                age,
                m_Link,
                m_LinkMessage ?? string.Empty,
                m_HasDrillMatrix,
                m_DrillFromTeeth,
                distanceScale,
                angleScale,
                m_HasContext,
                m_Context,
                m_HasThresholds,
                m_Thresholds,
                m_HasNavigationFrame,
                m_FrameSequence,
                m_CaptureTimeUnixMs,
                m_FrameValid,
                m_InvalidReason ?? string.Empty,
                m_HasLateralDirection,
                (float)m_LateralBuccalMm,
                (float)m_LateralMesialMm,
                m_HasTiltDirection,
                (float)m_TiltBuccalDeg,
                (float)m_TiltMesialDeg,
                m_HasDepthBreakdown,
                (float)m_CurrentDepthMm,
                m_HasDepthBreakdown ? (float)m_TargetDepthMm : (m_HasContext ? m_Context.TargetDepthMm : 0f),
                remainingDepthMm,
                m_DrillTipMm,
                m_DrillAxis,
                m_HasSliceState,
                m_SliceState,
                m_HasDisplayLayout,
                m_DisplayLayout,
                m_HasObservationControl,
                m_ObservationControl);
        }

        void ClearDynamicNavigation(string reason)
        {
            m_HasMetadata = false;
            m_HasNavigationFrame = false;
            m_FrameValid = false;
            m_LastMetadataRealtime = -1f;
            m_SourceAgeSecondsAtArrival = 0f;
            m_InvalidReason = reason ?? string.Empty;
            ClearFrameValues();
        }

        void ClearFrameValues()
        {
            m_Distance = 0;
            m_LateralDistance = 0;
            m_Angle = 0;
            m_RemainingDepthMm = 0;
            m_CurrentDepthMm = 0;
            m_HasLateralDirection = false;
            m_HasTiltDirection = false;
            m_HasDepthBreakdown = false;
            m_HasDrillMatrix = false;
            m_DrillFromTeeth = Matrix4x4.identity;
            m_DrillTipMm = Vector3.zero;
            m_DrillAxis = Vector3.zero;
        }

        bool MatchesActiveContext(string sessionId, ulong contextVersion)
        {
            return m_HasContext &&
                   contextVersion == m_Context.ContextVersion &&
                   string.Equals(sessionId ?? string.Empty, m_Context.SessionId, StringComparison.Ordinal);
        }

        static bool ContextIdentityEquals(DentalNavigationContext left, DentalNavigationContext right)
        {
            return string.Equals(left.CaseId, right.CaseId, StringComparison.Ordinal) &&
                   string.Equals(left.DatasetId, right.DatasetId, StringComparison.Ordinal) &&
                   string.Equals(left.CtId, right.CtId, StringComparison.Ordinal) &&
                   string.Equals(left.PlanId, right.PlanId, StringComparison.Ordinal) &&
                   string.Equals(left.ToothId, right.ToothId, StringComparison.Ordinal) &&
                   string.Equals(left.ToolId, right.ToolId, StringComparison.Ordinal) &&
                   string.Equals(left.StepId, right.StepId, StringComparison.Ordinal) &&
                   string.Equals(left.CoordinateFrameId, right.CoordinateFrameId, StringComparison.Ordinal) &&
                   Approximately(left.PlanEntryMm, right.PlanEntryMm) &&
                   Approximately(left.PlanAxis, right.PlanAxis) &&
                   Mathf.Approximately(left.TargetDepthMm, right.TargetDepthMm) &&
                   Approximately(left.BuccalAxis, right.BuccalAxis) &&
                   Approximately(left.MesialAxis, right.MesialAxis) &&
                   left.HasPatientFromDicom == right.HasPatientFromDicom &&
                   (!left.HasPatientFromDicom || Approximately(left.PatientFromDicom, right.PatientFromDicom));
        }

        static bool ThresholdsEqual(DentalNavigationThresholds left, DentalNavigationThresholds right)
        {
            return left.ContextVersion == right.ContextVersion &&
                   left.ConfigVersion == right.ConfigVersion &&
                   Mathf.Approximately(left.LateralGreenMaxMm, right.LateralGreenMaxMm) &&
                   Mathf.Approximately(left.LateralRedMinMm, right.LateralRedMinMm) &&
                   Mathf.Approximately(left.LateralHysteresisMm, right.LateralHysteresisMm) &&
                   Mathf.Approximately(left.AngleGreenMaxDeg, right.AngleGreenMaxDeg) &&
                   Mathf.Approximately(left.AngleRedMinDeg, right.AngleRedMinDeg) &&
                   Mathf.Approximately(left.AngleHysteresisDeg, right.AngleHysteresisDeg) &&
                   Mathf.Approximately(left.DepthApproachMm, right.DepthApproachMm) &&
                   Mathf.Approximately(left.DepthAtTargetToleranceMm, right.DepthAtTargetToleranceMm) &&
                   Mathf.Approximately(left.DepthOverrunRedMm, right.DepthOverrunRedMm) &&
                   Mathf.Approximately(left.DepthHysteresisMm, right.DepthHysteresisMm) &&
                   left.BoundaryRule == right.BoundaryRule;
        }

        static bool SliceStatesEqual(DentalSliceState left, DentalSliceState right)
        {
            return left.SyncEnabled == right.SyncEnabled &&
                   left.Source == right.Source &&
                   left.HasPlane == right.HasPlane &&
                   string.Equals(left.VolumeId, right.VolumeId, StringComparison.Ordinal) &&
                   string.Equals(left.FrameOfReferenceUid, right.FrameOfReferenceUid, StringComparison.Ordinal) &&
                   Approximately(left.PlaneOriginMm, right.PlaneOriginMm) &&
                   Approximately(left.PlaneNormal, right.PlaneNormal) &&
                   Approximately(left.PlaneUp, right.PlaneUp) &&
                   Mathf.Approximately(left.OffsetMm, right.OffsetMm) &&
                   left.SliceIndex == right.SliceIndex &&
                   string.Equals(left.SopInstanceUid, right.SopInstanceUid, StringComparison.Ordinal);
        }

        static bool DisplayLayoutsEqual(DentalDisplayLayoutState left, DentalDisplayLayoutState right)
        {
            return left.Source == right.Source &&
                   left.HudVisible == right.HudVisible &&
                   left.ModelVisible == right.ModelVisible &&
                   Approximately(left.HudPositionMeters, right.HudPositionMeters) &&
                   Approximately(left.ModelPositionMeters, right.ModelPositionMeters) &&
                   left.ResetToDefault == right.ResetToDefault;
        }

        static bool ObservationControlsEqual(DentalObservationControlState left, DentalObservationControlState right)
        {
            return left.MirrorEnabled == right.MirrorEnabled &&
                   left.RgbEnabled == right.RgbEnabled &&
                   string.Equals(left.ReceiverHost, right.ReceiverHost, StringComparison.Ordinal) &&
                   left.ReceiverPort == right.ReceiverPort &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.Fps == right.Fps;
        }

        static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.z, right.z);
        }

        static bool Approximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (var i = 0; i < 16; i++)
            {
                if (!Mathf.Approximately(left[i], right[i]))
                    return false;
            }
            return true;
        }

        static bool FrameValuesAreFinite(DentalNavigationFrameData frame)
        {
            return IsFinite(frame.DrillTipMm) &&
                   IsFinite(frame.DrillAxis) &&
                   frame.DrillAxis.sqrMagnitude > 0.000001f &&
                   (!frame.HasDrillMatrix || IsValidRigidTransform(frame.DrillFromTeeth)) &&
                   IsFinite(frame.LateralMm) &&
                   frame.LateralMm >= 0f &&
                   IsFinite(frame.LateralBuccalMm) &&
                   IsFinite(frame.LateralMesialMm) &&
                   IsFinite(frame.AngleDeg) &&
                   frame.AngleDeg >= 0f &&
                   IsFinite(frame.TiltBuccalDeg) &&
                   IsFinite(frame.TiltMesialDeg) &&
                   IsFinite(frame.CurrentDepthMm) &&
                   IsFinite(frame.TargetDepthMm) &&
                   frame.TargetDepthMm >= 0f &&
                   IsFinite(frame.RemainingDepthMm) &&
                   DirectionMagnitudeMatches(
                       frame.HasLateralDirection,
                       frame.LateralMm,
                       frame.LateralBuccalMm,
                       frame.LateralMesialMm) &&
                   DirectionMagnitudeMatches(
                       frame.HasTiltDirection,
                       frame.AngleDeg,
                       frame.TiltBuccalDeg,
                       frame.TiltMesialDeg) &&
                   (!frame.HasDepthBreakdown
                       || Mathf.Abs(frame.RemainingDepthMm
                           - (frame.TargetDepthMm - frame.CurrentDepthMm)) <= 0.05f);
        }

        static bool DirectionMagnitudeMatches(bool hasDirection, float total, float first, float second)
        {
            if (!hasDirection)
                return true;
            var componentMagnitude = Mathf.Sqrt(first * first + second * second);
            var tolerance = Mathf.Max(0.02f, total * 0.05f);
            return Mathf.Abs(componentMagnitude - total) <= tolerance;
        }

        static bool ContextValuesAreValid(DentalNavigationContext context)
        {
            return IsFinite(context.PlanEntryMm) &&
                   IsFinite(context.PlanAxis) &&
                   context.PlanAxis.sqrMagnitude > 0.000001f &&
                   IsFinite(context.TargetDepthMm) &&
                   context.TargetDepthMm >= 0f &&
                   IsFinite(context.BuccalAxis) &&
                   context.BuccalAxis.sqrMagnitude > 0.000001f &&
                   IsFinite(context.MesialAxis) &&
                   context.MesialAxis.sqrMagnitude > 0.000001f &&
                   (!context.HasPatientFromDicom || IsValidRigidTransform(context.PatientFromDicom));
        }

        static bool SliceValuesAreValid(DentalSliceState sliceState)
        {
            if (!sliceState.HasPlane)
                return sliceState.SliceIndex >= 0;

            if (string.IsNullOrWhiteSpace(sliceState.VolumeId)
                || !IsFinite(sliceState.PlaneOriginMm)
                || !IsFinite(sliceState.PlaneNormal)
                || !IsFinite(sliceState.PlaneUp)
                || !IsFinite(sliceState.OffsetMm)
                || sliceState.PlaneNormal.sqrMagnitude <= 0.000001f
                || sliceState.PlaneUp.sqrMagnitude <= 0.000001f)
                return false;

            return Vector3.Cross(sliceState.PlaneNormal, sliceState.PlaneUp).sqrMagnitude > 0.000001f;
        }

        internal static bool IsValidRigidTransform(Matrix4x4 matrix)
        {
            for (var index = 0; index < 16; index++)
            {
                if (!IsFinite(matrix[index]))
                    return false;
            }

            const float affineTolerance = 0.0001f;
            if (Mathf.Abs(matrix[3, 0]) > affineTolerance
                || Mathf.Abs(matrix[3, 1]) > affineTolerance
                || Mathf.Abs(matrix[3, 2]) > affineTolerance
                || Mathf.Abs(matrix[3, 3] - 1f) > affineTolerance)
                return false;

            // Both protocol coordinate systems use millimetres. Accept rotations and handedness
            // reflections, but reject scale/shear because those would make direction transforms
            // and clinical distances ambiguous.
            var x = new Vector3(matrix[0, 0], matrix[1, 0], matrix[2, 0]);
            var y = new Vector3(matrix[0, 1], matrix[1, 1], matrix[2, 1]);
            var z = new Vector3(matrix[0, 2], matrix[1, 2], matrix[2, 2]);
            const float orthogonalTolerance = 0.002f;
            return Mathf.Abs(x.sqrMagnitude - 1f) <= orthogonalTolerance
                && Mathf.Abs(y.sqrMagnitude - 1f) <= orthogonalTolerance
                && Mathf.Abs(z.sqrMagnitude - 1f) <= orthogonalTolerance
                && Mathf.Abs(Vector3.Dot(x, y)) <= orthogonalTolerance
                && Mathf.Abs(Vector3.Dot(x, z)) <= orthogonalTolerance
                && Mathf.Abs(Vector3.Dot(y, z)) <= orthogonalTolerance
                && Mathf.Abs(Mathf.Abs(matrix.determinant) - 1f) <= 0.005f;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null)
                return;

            handler(Capture(Time.realtimeSinceStartup));
        }

        internal static Matrix4x4 ToUnityMatrix(IList<double> values)
        {
            var matrix = Matrix4x4.identity;
            if (values == null || values.Count == 0)
                return matrix;
            if (values.Count != 16)
                return Matrix4x4.zero;

            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                    matrix[row, col] = (float)values[row * 4 + col];
            }

            return matrix;
        }
    }
}
