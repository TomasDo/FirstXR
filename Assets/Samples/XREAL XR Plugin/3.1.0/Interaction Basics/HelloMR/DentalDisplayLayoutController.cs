using System;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Authoritative, persistent head-locked positions for the CT/HUD group and
    /// the independent 3D model. Navigation-side control versions always win.
    /// </summary>
    public sealed class DentalDisplayLayoutController : MonoBehaviour
    {
        const string PrefPrefix = "DentalNavigation.Layout.v2.";
        static readonly Vector3 DefaultHudPosition = new Vector3(0f, -0.13f, 1.80f);
        static readonly Vector3 DefaultModelPosition = new Vector3(0.32f, -0.024f, 1.80f);

        static DentalDisplayLayoutController s_Instance;

        ulong m_ControlVersion;
        string m_SessionId = string.Empty;
        ulong m_ContextVersion;
        Vector3 m_HudPosition = DefaultHudPosition;
        Vector3 m_ModelPosition = DefaultModelPosition;
        bool m_HudVisible = true;
        bool m_ModelVisible;

        public static DentalDisplayLayoutController Instance => s_Instance;
        public ulong ControlVersion => m_ControlVersion;
        public Vector3 HudLocalPositionMeters => m_HudPosition;
        public Vector3 ModelLocalPositionMeters => m_ModelPosition;
        public bool HudVisible => m_HudVisible;
        public bool ModelVisible => m_ModelVisible;

        public event Action Changed;

        public static DentalDisplayLayoutController EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = FindObjectOfType<DentalDisplayLayoutController>();
            if (existing != null)
            {
                s_Instance = existing;
                return existing;
            }

            var go = new GameObject("Dental Display Layout");
            s_Instance = go.AddComponent<DentalDisplayLayoutController>();
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
            Load();
        }

        void OnEnable()
        {
            var state = DentalNavigationState.EnsureInstance();
            state.DisplayLayoutChanged -= OnDisplayLayoutChanged;
            state.DisplayLayoutChanged += OnDisplayLayoutChanged;
            state.ContextChanged -= OnContextChanged;
            state.ContextChanged += OnContextChanged;
            var snapshot = state.Capture(Time.realtimeSinceStartup);
            if (snapshot.HasContext)
                OnContextChanged(snapshot.Context);
            if (snapshot.HasDisplayLayout)
                ApplyRemote(snapshot.DisplayLayout);
        }

        void OnDisable()
        {
            if (DentalNavigationState.Instance != null)
            {
                DentalNavigationState.Instance.DisplayLayoutChanged -= OnDisplayLayoutChanged;
                DentalNavigationState.Instance.ContextChanged -= OnContextChanged;
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void OnDisplayLayoutChanged(DentalDisplayLayoutState layout) => ApplyRemote(layout);

        void OnContextChanged(DentalNavigationContext context)
        {
            if (string.Equals(m_SessionId, context.SessionId, StringComparison.Ordinal)
                && m_ContextVersion == context.ContextVersion)
                return;
            m_SessionId = context.SessionId;
            m_ContextVersion = context.ContextVersion;
            m_ControlVersion = 0;
        }

        public bool ApplyRemote(DentalDisplayLayoutState value)
        {
            if (!string.Equals(m_SessionId, value.SessionId, StringComparison.Ordinal)
                || m_ContextVersion != value.ContextVersion)
            {
                m_SessionId = value.SessionId;
                m_ContextVersion = value.ContextVersion;
                m_ControlVersion = 0;
            }
            if (value.ControlVersion < m_ControlVersion)
                return false;

            if (!value.ResetToDefault
                && (!IsFinite(value.HudPositionMeters) || !IsFinite(value.ModelPositionMeters)))
                return false;

            var hudPosition = value.ResetToDefault
                ? DefaultHudPosition
                : ClampPosition(value.HudPositionMeters);
            var modelPosition = value.ResetToDefault
                ? DefaultModelPosition
                : ClampPosition(value.ModelPositionMeters);
            if (value.ControlVersion == m_ControlVersion
                && Approximately(m_HudPosition, hudPosition)
                && Approximately(m_ModelPosition, modelPosition)
                && m_HudVisible == value.HudVisible
                && m_ModelVisible == value.ModelVisible)
                return true;

            m_ControlVersion = value.ControlVersion;
            m_HudPosition = hudPosition;
            m_ModelPosition = modelPosition;
            m_HudVisible = value.HudVisible;
            m_ModelVisible = value.ModelVisible;
            Save();
            Changed?.Invoke();
            return true;
        }

        public DentalDisplayLayoutState CaptureAppliedState(DentalControlSource source)
        {
            return new DentalDisplayLayoutState(
                m_SessionId,
                m_ContextVersion,
                m_ControlVersion,
                source,
                m_HudVisible,
                m_ModelVisible,
                m_HudPosition,
                m_ModelPosition,
                false);
        }

        public void SetHudVisibleLocally(bool visible)
        {
            if (m_HudVisible == visible)
                return;
            m_HudVisible = visible;
            Save();
            Changed?.Invoke();
        }

        public void SetModelVisibleLocally(bool visible)
        {
            if (m_ModelVisible == visible)
                return;
            m_ModelVisible = visible;
            Save();
            Changed?.Invoke();
        }

        public void SetPositionsLocally(Vector3 hudPositionMeters, Vector3 modelPositionMeters)
        {
            if (!IsFinite(hudPositionMeters) || !IsFinite(modelPositionMeters))
                return;
            m_HudPosition = ClampPosition(hudPositionMeters);
            m_ModelPosition = ClampPosition(modelPositionMeters);
            Save();
            Changed?.Invoke();
        }

        public void ResetToDefaults()
        {
            m_HudPosition = DefaultHudPosition;
            m_ModelPosition = DefaultModelPosition;
            m_HudVisible = true;
            m_ModelVisible = false;
            Save();
            Changed?.Invoke();
        }

        static Vector3 ClampPosition(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -1.2f, 1.2f),
                Mathf.Clamp(position.y, -1.0f, 0.8f),
                Mathf.Clamp(position.z, 0.40f, 4.0f));
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x + value.y + value.z)
                && !float.IsInfinity(value.x)
                && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z);
        }

        static bool Approximately(Vector3 left, Vector3 right) =>
            (left - right).sqrMagnitude <= 0.00000001f;

        void Load()
        {
            m_HudPosition = LoadVector("hud", DefaultHudPosition);
            m_ModelPosition = LoadVector("model", DefaultModelPosition);
            m_HudVisible = PlayerPrefs.GetInt(PrefPrefix + "hud.visible", 1) != 0;
            // Implant-navigation product default: the optional 3D widget is hidden.
            m_ModelVisible = PlayerPrefs.GetInt(PrefPrefix + "model.visible", 0) != 0;
        }

        void Save()
        {
            SaveVector("hud", m_HudPosition);
            SaveVector("model", m_ModelPosition);
            PlayerPrefs.SetInt(PrefPrefix + "hud.visible", m_HudVisible ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "model.visible", m_ModelVisible ? 1 : 0);
            PlayerPrefs.Save();
        }

        static Vector3 LoadVector(string key, Vector3 fallback)
        {
            return new Vector3(
                PlayerPrefs.GetFloat(PrefPrefix + key + ".x", fallback.x),
                PlayerPrefs.GetFloat(PrefPrefix + key + ".y", fallback.y),
                PlayerPrefs.GetFloat(PrefPrefix + key + ".z", fallback.z));
        }

        static void SaveVector(string key, Vector3 value)
        {
            PlayerPrefs.SetFloat(PrefPrefix + key + ".x", value.x);
            PlayerPrefs.SetFloat(PrefPrefix + key + ".y", value.y);
            PlayerPrefs.SetFloat(PrefPrefix + key + ".z", value.z);
        }
    }
}
