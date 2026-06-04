using Unity.XR.XREAL;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    public sealed class HelloMRVoiceCommandBridge : MonoBehaviour
    {
        HelloMR m_HelloMR;
        VoiceCommandRecognizer m_Recognizer;

        void Awake()
        {
            m_HelloMR = FindObjectOfType<HelloMR>();
            m_Recognizer = FindObjectOfType<VoiceCommandRecognizer>();
        }

        public bool TryExecute(string action)
        {
            if (string.IsNullOrEmpty(action))
                return false;

            if (m_HelloMR == null)
                m_HelloMR = FindObjectOfType<HelloMR>();

            switch (action)
            {
                case "ChangeToHandInput":
                    m_HelloMR?.ChangeToHandInput();
                    return m_HelloMR != null;
                case "ChangeToControllerInput":
                    m_HelloMR?.ChangeToControllerInput();
                    return m_HelloMR != null;
                case "ApplyControllerWithHandVisualizationOnly":
                    m_HelloMR?.ApplyControllerWithHandVisualizationOnly();
                    return m_HelloMR != null;
                case "ShowGlassesControlWindow":
                    m_HelloMR?.SetGlassesControlWindowVisible(true);
                    return m_HelloMR != null;
                case "HideGlassesControlWindow":
                    m_HelloMR?.SetGlassesControlWindowVisible(false);
                    return m_HelloMR != null;
                case "ToggleGlassesControlWindow":
                    m_HelloMR?.ToggleGlassesControlWindow();
                    return m_HelloMR != null;
                case "MoveXPlus":
                    return MoveReference(Vector3.right);
                case "MoveXMinus":
                    return MoveReference(Vector3.left);
                case "MoveYPlus":
                    return MoveReference(Vector3.up);
                case "MoveYMinus":
                    return MoveReference(Vector3.down);
                case "MoveZPlus":
                    return MoveReference(Vector3.forward);
                case "MoveZMinus":
                    return MoveReference(Vector3.back);
                case "Vibrate":
                    m_HelloMR?.Vibrate();
                    return m_HelloMR != null;
                case "SetTracking6Dof":
                    m_HelloMR?.SetTrackingMode(TrackingType.MODE_6DOF);
                    return m_HelloMR != null;
                case "SetTracking3Dof":
                    m_HelloMR?.SetTrackingMode(TrackingType.MODE_3DOF);
                    return m_HelloMR != null;
                case "SetTracking0Dof":
                    m_HelloMR?.SetTrackingMode(TrackingType.MODE_0DOF);
                    return m_HelloMR != null;
                case "SetTracking0DofStable":
                    m_HelloMR?.SetTrackingMode(TrackingType.MODE_0DOF_STAB);
                    return m_HelloMR != null;
                case "PauseVoiceListen":
                    m_Recognizer?.SetListeningEnabled(false);
                    return m_Recognizer != null;
                case "ResumeVoiceListen":
                    m_Recognizer?.SetListeningEnabled(true);
                    return m_Recognizer != null;
                case "ClearVoiceCommandLog":
                    m_Recognizer?.ClearLog();
                    return m_Recognizer != null;
                default:
                    Debug.LogWarning($"[VoiceCmd] Unknown action: {action}");
                    return false;
            }
        }

        bool MoveReference(Vector3 direction)
        {
            if (ReferenceCubeSpawner.Instance == null)
                return false;

            ReferenceCubeSpawner.Instance.MoveTargetsByDirection(direction);
            return true;
        }
    }
}
