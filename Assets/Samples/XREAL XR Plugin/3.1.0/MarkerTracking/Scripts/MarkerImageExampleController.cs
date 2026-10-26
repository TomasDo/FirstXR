using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Unity.XR.XREAL.Samples
{
    /// <summary> 
    /// Controller for TrackingImage example. 
    /// </summary>
    [HelpURL("https://developer.xreal.com/develop/unity/image-tracking")]
    public class MarkerImageExampleController : MonoBehaviour
    {
        public event Action<ARTrackedImage> OnImageLoaded;
        public event Action<ARTrackedImage> OnImageLost;

        public ARTrackedImageManager m_TrackedImageManager;
        /// <summary> 
        /// A prefab for visualizing an TrackingImage. 
        /// </summary>
        public MarkerImageVisualizer MarkerImageVisualizerPrefab;

        [Header("Debug")]
        [SerializeField]
        bool m_ShowDebugPanel = true;

        [SerializeField]
        Rect m_DebugPanelRect = new Rect(24f, 24f, 760f, 460f);

        private Dictionary<int, MarkerImageVisualizer> m_Visualizers
            = new Dictionary<int, MarkerImageVisualizer>();

        private List<ARTrackedImage> m_TempTrackingImages = new List<ARTrackedImage>();
        private readonly StringBuilder m_DebugBuilder = new StringBuilder(1024);

        int m_AddedCount;
        int m_UpdatedCount;
        int m_RemovedCount;
        int m_LastMarkerIndex = -1;
        string m_LastEvent = "none";
        string m_LastTrackingState = "none";
        GUIStyle m_DebugLabelStyle;

        private void Start()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
#if UNITY_EDITOR
            MockTrackableImageFactory.CreateSingleton().OnTrackablesChanged += OnMockTrackablesChanged;
#endif
        }

        private void OnDestroy()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
#if UNITY_EDITOR
            if (MockTrackableImageFactory.Singleton != null)
                MockTrackableImageFactory.Singleton.OnTrackablesChanged -= OnMockTrackablesChanged;
#endif
        }



        private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
        {
            foreach (var removed in args.removed)
            {
                RecordMarkerEvent("removed", removed);
                m_RemovedCount++;
                OnImageLost?.Invoke(removed);
            }

            foreach (var updated in args.updated)
            {
                RecordMarkerEvent("updated", updated);
                m_UpdatedCount++;
            }

            foreach (var added in args.added)
            {
                RecordMarkerEvent("added", added);
                m_AddedCount++;
                OnImageLoaded?.Invoke(added);
            }

        }

#if UNITY_EDITOR
        private void OnMockTrackablesChanged(List<ARTrackedImage> removedList, List<ARTrackedImage> addedList)
        {
            foreach (var removed in removedList)
            {
                RecordMarkerEvent("mock removed", removed);
                m_RemovedCount++;
                OnImageLost?.Invoke(removed);
            }
            foreach (var added in addedList)
            {
                RecordMarkerEvent("mock added", added);
                m_AddedCount++;
                OnImageLoaded?.Invoke(added);
            }

        }
#endif

        void RecordMarkerEvent(string eventName, ARTrackedImage image)
        {
            m_LastEvent = eventName;
            m_LastMarkerIndex = GetMarkerIndex(image);
            m_LastTrackingState = image != null ? image.trackingState.ToString() : "null";
            Debug.Log($"[MarkerDebug] {eventName} index={m_LastMarkerIndex} state={m_LastTrackingState}");
        }

        int GetMarkerIndex(ARTrackedImage image)
        {
            if (image == null)
                return -1;

            return (int)image.trackableId.subId2 & 0xFF;
        }

        void OnGUI()
        {
            if (!m_ShowDebugPanel)
                return;

#if !UNITY_EDITOR
            if (Application.platform != RuntimePlatform.Android)
                return;
#endif

            EnsureDebugStyle();

            GUI.Box(m_DebugPanelRect, string.Empty);
            GUILayout.BeginArea(new Rect(
                m_DebugPanelRect.x + 16f,
                m_DebugPanelRect.y + 14f,
                m_DebugPanelRect.width - 32f,
                m_DebugPanelRect.height - 28f));

            GUILayout.Label(BuildDebugText(), m_DebugLabelStyle);
            GUILayout.EndArea();
        }

        void EnsureDebugStyle()
        {
            if (m_DebugLabelStyle != null)
                return;

            m_DebugLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                wordWrap = true
            };
            m_DebugLabelStyle.normal.textColor = Color.yellow;
        }

        string BuildDebugText()
        {
            m_DebugBuilder.Clear();
            m_DebugBuilder.AppendLine("MarkerTracking Debug");
            m_DebugBuilder.AppendLine($"Screen: {Screen.width}x{Screen.height}");
            m_DebugBuilder.AppendLine($"Manager: {(m_TrackedImageManager != null ? "ok" : "missing")}");

            if (m_TrackedImageManager != null)
            {
                var subsystem = m_TrackedImageManager.subsystem;
                m_DebugBuilder.AppendLine($"Manager enabled: {m_TrackedImageManager.enabled}");
                m_DebugBuilder.AppendLine($"Subsystem: {(subsystem != null ? subsystem.GetType().Name : "null")}");
                m_DebugBuilder.AppendLine($"Subsystem running: {(subsystem != null && subsystem.running)}");
                var referenceLibrary = m_TrackedImageManager.referenceLibrary;
                m_DebugBuilder.AppendLine($"Reference library: {(referenceLibrary != null ? referenceLibrary.GetType().Name : "null")}");
                m_DebugBuilder.AppendLine($"Reference image count: {(referenceLibrary != null ? referenceLibrary.count : 0)}");
            }

            m_DebugBuilder.AppendLine($"Events added/updated/removed: {m_AddedCount}/{m_UpdatedCount}/{m_RemovedCount}");
            m_DebugBuilder.AppendLine($"Last event: {m_LastEvent}");
            m_DebugBuilder.AppendLine($"Last marker index: {m_LastMarkerIndex}");
            m_DebugBuilder.AppendLine($"Last tracking state: {m_LastTrackingState}");
            m_DebugBuilder.AppendLine("Tracked now:");

            var visibleCount = 0;
            if (m_TrackedImageManager != null)
            {
                foreach (var image in m_TrackedImageManager.trackables)
                {
                    visibleCount++;
                    m_DebugBuilder.AppendLine($"  #{GetMarkerIndex(image)} {image.trackingState} pos={image.transform.position:F2}");
                    if (visibleCount >= 8)
                    {
                        m_DebugBuilder.AppendLine("  ...");
                        break;
                    }
                }
            }

            if (visibleCount == 0)
                m_DebugBuilder.AppendLine("  none");

            return m_DebugBuilder.ToString();
        }
    }
}
