using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.XR.XREAL;
#if XR_HANDS
using UnityEngine.XR.Hands;
#endif

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// On-device overlay for narrowing hand tracking failures (subsystem, isTracked, RGB camera).
    /// </summary>
    public class HandTrackingDiagnostics : MonoBehaviour
    {
        [SerializeField]
        bool m_ShowOverlayOnBeamPro = true;

        [SerializeField]
        float m_LogIntervalSeconds = 2f;

        [SerializeField]
        bool m_LogToConsole = true;

        readonly StringBuilder m_Builder = new StringBuilder(640);

        string m_DisplayText = "Hand diag: starting...";
        float m_NextLogTime;
        XREALRGBCameraPlugState m_RgbPlugState = XREALRGBCameraPlugState.UNKNOWN;
        bool m_SubscribedRgb;
        GUIStyle m_OverlayStyle;
        static readonly Color s_OverlayTextColor = new Color(0.2f, 1f, 0.35f);

        void OnEnable()
        {
            if (m_SubscribedRgb)
                return;

            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState += OnRgbPlugState;
            m_SubscribedRgb = true;
        }

        void OnDisable()
        {
            if (!m_SubscribedRgb)
                return;

            XREALCallbackHandler.OnXREALGlassesRGBCameraPlugState -= OnRgbPlugState;
            m_SubscribedRgb = false;
        }

        void OnRgbPlugState(XREALRGBCameraPlugState state) => m_RgbPlugState = state;

        void Update()
        {
            RefreshStatus();

            if (!m_LogToConsole || Time.unscaledTime < m_NextLogTime)
                return;

            m_NextLogTime = Time.unscaledTime + m_LogIntervalSeconds;
            Debug.Log($"[HandDiag] {m_DisplayText.Replace("\n", " | ")}");
        }

        void RefreshStatus()
        {
            m_Builder.Clear();
            m_Builder.AppendLine("=== Hand Tracking ===");
            m_Builder.AppendLine($"Input: {XREALPlugin.GetInputSource()}");
            m_Builder.AppendLine($"Track: {XREALPlugin.GetTrackingType()}");
            m_Builder.AppendLine($"HandSupported: {XREALPlugin.IsHandTrackingSupported()}");
            m_Builder.AppendLine($"RGB plug: {m_RgbPlugState}");
            m_Builder.AppendLine($"RGB feature: {XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA)}");

            var rgb = XREALRGBCameraTexture.Singleton;
            m_Builder.AppendLine(rgb != null
                ? $"RGB capturing: {rgb.IsCapturing}"
                : "RGB texture: (singleton null)");

            AppendVisualizerLine("Left Hand Tracking");
            AppendVisualizerLine("Right Hand Tracking");

#if XR_HANDS
            var handSubsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(handSubsystems);
            m_Builder.AppendLine($"XRHandSubsystem count: {handSubsystems.Count}");

            if (handSubsystems.Count == 0)
            {
                m_Builder.AppendLine("-> No subsystem (package/loader/build)");
            }
            else
            {
                var sub = handSubsystems[0];
                m_Builder.AppendLine($"id: {sub.subsystemDescriptor.id}");
                m_Builder.AppendLine($"running: {sub.running}");
                m_Builder.AppendLine($"L isTracked: {sub.leftHand.isTracked}");
                m_Builder.AppendLine($"R isTracked: {sub.rightHand.isTracked}");

                if (!sub.running)
                    m_Builder.AppendLine("-> Subsystem not running");
                else if (!sub.leftHand.isTracked && !sub.rightHand.isTracked)
                    m_Builder.AppendLine("-> No hand tracked (RGB FOV / native)");
            }

            AppendEventsLine("Left Hand Tracking");
            AppendEventsLine("Right Hand Tracking");
#else
            m_Builder.AppendLine("XR_HANDS undefined in build");
#endif

            m_DisplayText = m_Builder.ToString();
        }

        void AppendVisualizerLine(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                m_Builder.AppendLine($"{objectName}: NOT FOUND");
                return;
            }

            m_Builder.AppendLine($"{objectName}: active={go.activeInHierarchy}");
        }

#if XR_HANDS
        void AppendEventsLine(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
                return;

            var events = go.GetComponent<XRHandTrackingEvents>();
            if (events == null)
            {
                m_Builder.AppendLine($"{objectName} events: missing component");
                return;
            }

            m_Builder.AppendLine($"{objectName} handIsTracked: {events.handIsTracked}");
        }
#endif

        void OnGUI()
        {
            if (!m_ShowOverlayOnBeamPro || Application.platform != RuntimePlatform.Android)
                return;

            const float width = 520f;
            var height = Mathf.Min(Screen.height * 0.45f, 380f);
            var x = (Screen.width - width) * 0.5f;
            var y = (Screen.height - height) * 0.5f;
            var rect = new Rect(x, y, width, height);

            EnsureOverlayStyle();

            GUI.depth = 10;
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f), m_DisplayText, m_OverlayStyle);
        }

        void EnsureOverlayStyle()
        {
            var fontSize = Mathf.Max(14, Screen.height / 64);
            if (m_OverlayStyle != null && m_OverlayStyle.fontSize == fontSize)
                return;

            m_OverlayStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                wordWrap = true,
                richText = false,
                clipping = TextClipping.Overflow
            };
            m_OverlayStyle.normal.textColor = s_OverlayTextColor;
        }
    }
}
