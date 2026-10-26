using Dentalmodeltransfer;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public partial class DentalRobotGrpcClient
    {
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
                unitsValid ? message.InvalidReason : "Navigation units must be mm and °",
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

    }
}
