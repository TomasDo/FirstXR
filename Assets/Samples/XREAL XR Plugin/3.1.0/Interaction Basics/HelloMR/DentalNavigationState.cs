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

        public DentalNavigationSnapshot(
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
            float angleToDegrees)
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
        }

        public bool IsFresh => HasMetadata && Link == DentalLinkState.Live && AgeSeconds <= 0.20f;
        public bool IsAging => HasMetadata && Link == DentalLinkState.Live && AgeSeconds > 0.20f && AgeSeconds <= 0.50f;
        public bool IsStale => HasMetadata && AgeSeconds > 0.50f && AgeSeconds <= 1.00f;
        public bool HideNumbers => Link == DentalLinkState.Lost || Link == DentalLinkState.Idle || !HasMetadata || AgeSeconds > 1.00f;
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
        DentalLinkState m_Link = DentalLinkState.Idle;
        string m_LinkMessage = string.Empty;
        bool m_HasDrillMatrix;
        Matrix4x4 m_DrillFromTeeth = Matrix4x4.identity;
        DentalHudEvaluation m_LastEvaluation;
        bool m_LoggedFirstMetadata;

        public static DentalNavigationState Instance => s_Instance;

        public DentalHudEvaluation LastEvaluation => m_LastEvaluation;

        public event Action<DentalNavigationSnapshot> Changed;

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
            m_Link = DentalLinkState.Connecting;
            m_LinkMessage = string.IsNullOrEmpty(endpoint) ? "连接中" : endpoint;
            RaiseChanged();
        }

        public void NotifyDisconnected(string reason)
        {
            m_Link = DentalLinkState.Lost;
            m_LinkMessage = string.IsNullOrEmpty(reason) ? "未连接机器人" : reason;
            RaiseChanged();
        }

        public void ApplyMetadata(
            string datasetId,
            IList<double> drillFromTeeth,
            double distance,
            double lateralDistance,
            double angle)
        {
            m_HasMetadata = true;
            m_DatasetId = string.IsNullOrEmpty(datasetId) ? string.Empty : datasetId;
            m_Distance = distance;
            m_LateralDistance = lateralDistance;
            m_Angle = angle;
            m_LastMetadataRealtime = Time.realtimeSinceStartup;
            m_Link = DentalLinkState.Live;
            m_LinkMessage = "已连接";
            m_HasDrillMatrix = drillFromTeeth != null && drillFromTeeth.Count >= 16;
            m_DrillFromTeeth = ToUnityMatrix(drillFromTeeth);

            if (!m_LoggedFirstMetadata)
            {
                m_LoggedFirstMetadata = true;
                Debug.Log(
                    $"DentalNavigationState: first metadata dataset={m_DatasetId}, " +
                    $"depthMm={m_Distance * m_DistanceToMillimeters:0.###}, " +
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
            var age = m_HasMetadata && m_LastMetadataRealtime >= 0f
                ? Mathf.Max(0f, nowRealtime - m_LastMetadataRealtime)
                : float.PositiveInfinity;

            return new DentalNavigationSnapshot(
                m_HasMetadata,
                string.IsNullOrEmpty(m_DatasetId) ? "—" : m_DatasetId,
                (float)(m_Distance * m_DistanceToMillimeters),
                (float)(Math.Abs(m_LateralDistance) * m_DistanceToMillimeters),
                (float)(Math.Abs(m_Angle) * m_AngleToDegrees),
                age,
                m_Link,
                m_LinkMessage ?? string.Empty,
                m_HasDrillMatrix,
                m_DrillFromTeeth,
                m_DistanceToMillimeters,
                m_AngleToDegrees);
        }

        void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null)
                return;

            handler(Capture(Time.realtimeSinceStartup));
        }

        static Matrix4x4 ToUnityMatrix(IList<double> drillFromTeeth)
        {
            var matrix = Matrix4x4.identity;
            if (drillFromTeeth == null || drillFromTeeth.Count < 16)
                return matrix;

            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                    matrix[row, col] = (float)drillFromTeeth[row * 4 + col];
            }

            return matrix;
        }
    }
}
