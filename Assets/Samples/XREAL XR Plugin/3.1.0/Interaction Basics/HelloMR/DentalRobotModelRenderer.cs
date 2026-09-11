using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    public class DentalRobotModelRenderer : MonoBehaviour
    {
        public const long MaxStlAssetBytes = 128L * 1024L * 1024L;

        public enum DentalModelType
        {
            Unknown = 0,
            Teeth = 1,
            Drill = 2,
        }

        class ModelBuffer
        {
            public byte[] Bytes = Array.Empty<byte>();
            public string FileName = string.Empty;
            public long HighestReceivedOffset;
            public long ExpectedBytes;
            public bool ManifestTransferActive;

            public void Clear()
            {
                Bytes = Array.Empty<byte>();
                FileName = string.Empty;
                HighestReceivedOffset = 0;
                ExpectedBytes = 0;
                ManifestTransferActive = false;
            }
        }

        [SerializeField]
        Vector3 m_HeadLockedLocalPosition = new Vector3(0.320f, -0.024f, 1.80f);

        [SerializeField]
        float m_ModelMaxSizeMeters = 0.11f;

        [SerializeField]
        Color m_TeethColor = new Color(0.85f, 0.85f, 0.82f, 0.35f);

        [SerializeField]
        Color m_DrillColor = new Color(0.361f, 0.882f, 1f, 1f);

        [SerializeField]
        bool m_WidgetVisible = false;

        static DentalRobotModelRenderer s_Instance;

        readonly Dictionary<DentalModelType, ModelBuffer> m_Buffers = new Dictionary<DentalModelType, ModelBuffer>();

        Transform m_Root;
        Transform m_TeethTransform;
        Transform m_DrillTransform;
        LineRenderer m_AxisLine;
        Vector3 m_TeethSceneCenter;
        float m_ModelScale = 1f;
        Material m_TeethMaterial;
        Material m_DrillMaterial;
        Mesh m_TeethMesh;
        Mesh m_DrillMesh;
        bool m_HasMetadata;
        Matrix4x4 m_DrillMatrix = Matrix4x4.identity;
        Camera m_Camera;

        public static DentalRobotModelRenderer Instance => s_Instance;

        public bool WidgetVisible => m_WidgetVisible;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            DentalDisplayLayoutController.EnsureInstance();
            m_Buffers[DentalModelType.Teeth] = new ModelBuffer();
            m_Buffers[DentalModelType.Drill] = new ModelBuffer();
        }

        void OnEnable()
        {
            var state = DentalNavigationState.EnsureInstance();
            state.Changed -= OnNavigationChanged;
            state.Changed += OnNavigationChanged;
        }

        void OnDisable()
        {
            if (DentalNavigationState.Instance != null)
                DentalNavigationState.Instance.Changed -= OnNavigationChanged;
        }

        void Start()
        {
            StartCoroutine(CreateRootWhenCameraReady());
        }

        void LateUpdate()
        {
            AttachToCameraIfNeeded();
            ApplyLayoutAndValidity();
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            if (DentalNavigationState.Instance != null)
                DentalNavigationState.Instance.Changed -= OnNavigationChanged;

            if (m_TeethMaterial != null)
                Destroy(m_TeethMaterial);

            if (m_DrillMaterial != null)
                Destroy(m_DrillMaterial);

            if (m_TeethMesh != null)
                Destroy(m_TeethMesh);

            if (m_DrillMesh != null)
                Destroy(m_DrillMesh);

            if (m_AxisLine != null && m_AxisLine.material != null)
                Destroy(m_AxisLine.material);
        }

        IEnumerator CreateRootWhenCameraReady()
        {
            Camera camera = null;
            for (var i = 0; i < 120 && camera == null; i++)
            {
                camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
                if (camera != null)
                    break;

                yield return null;
            }

            if (camera == null)
            {
                Debug.LogWarning("DentalRobotModelRenderer: No main camera found.");
                yield break;
            }

            EnsureRoot(camera);
        }

        void OnNavigationChanged(DentalNavigationSnapshot snap)
        {
            var valid = snap.HasDrillMatrix
                && snap.Link == DentalLinkState.Live
                && !snap.HideNumbers
                && (!snap.HasNavigationFrame || snap.FrameValid);
            if (!valid)
            {
                m_HasMetadata = false;
                SetRealtimeObjectsVisible(false);
                return;
            }

            m_HasMetadata = true;
            m_DrillMatrix = snap.DrillFromTeeth;
            SetRealtimeObjectsVisible(true);
            ApplyDrillTransformFromMetadata();
        }

        public void SetWidgetVisible(bool visible)
        {
            m_WidgetVisible = visible;
            var layout = DentalDisplayLayoutController.Instance;
            if (layout != null && layout.ModelVisible != visible)
                layout.SetModelVisibleLocally(visible);
            if (m_Root != null)
                m_Root.gameObject.SetActive(visible);
        }

        public void ApplyMetadata(IList<double> drillFromTeeth)
        {
            m_HasMetadata = drillFromTeeth != null && drillFromTeeth.Count >= 16;
            if (!m_HasMetadata)
                return;

            m_DrillMatrix = Matrix4x4.identity;
            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                    m_DrillMatrix[row, col] = (float)drillFromTeeth[row * 4 + col];
            }

            ApplyDrillTransformFromMetadata();
        }

        public void ApplyStlChunk(DentalModelType modelType, string filename, long offset, byte[] data)
        {
            TryApplyStlChunk(modelType, filename, offset, data);
        }

        public bool TryApplyStlChunk(DentalModelType modelType, string filename, long offset, byte[] data)
        {
            if (modelType != DentalModelType.Teeth && modelType != DentalModelType.Drill)
                return false;

            if (data == null || data.Length == 0)
                return false;

            var buffer = m_Buffers[modelType];
            if (!buffer.ManifestTransferActive && offset == 0 && buffer.Bytes.Length > 0)
                buffer.Clear();

            buffer.FileName = string.IsNullOrEmpty(filename) ? modelType.ToString() + ".stl" : filename;
            return CopyChunk(buffer, offset, data);
        }

        public bool BeginStlAsset(DentalModelType modelType, string filename, long expectedBytes)
        {
            if ((modelType != DentalModelType.Teeth && modelType != DentalModelType.Drill)
                || expectedBytes <= 0 || expectedBytes > MaxStlAssetBytes)
                return false;

            var buffer = m_Buffers[modelType];
            var normalizedFilename = string.IsNullOrEmpty(filename) ? modelType + ".stl" : filename;
            if (buffer.ManifestTransferActive
                && buffer.ExpectedBytes == expectedBytes
                && string.Equals(buffer.FileName, normalizedFilename, StringComparison.Ordinal)
                && buffer.Bytes.LongLength == expectedBytes)
                return true;

            buffer.Clear();
            try
            {
                buffer.Bytes = new byte[(int)expectedBytes];
                buffer.FileName = normalizedFilename;
                buffer.ExpectedBytes = expectedBytes;
                buffer.ManifestTransferActive = true;
                return true;
            }
            catch (OutOfMemoryException)
            {
                buffer.Clear();
                return false;
            }
        }

        public bool TryCopyStlAssetBytes(
            DentalModelType modelType,
            long expectedBytes,
            out byte[] bytes,
            out string error)
        {
            bytes = null;
            error = string.Empty;
            if (!m_Buffers.TryGetValue(modelType, out var buffer))
            {
                error = "STL model type is unavailable.";
                return false;
            }
            if (!buffer.ManifestTransferActive || buffer.ExpectedBytes != expectedBytes
                || expectedBytes <= 0 || expectedBytes > MaxStlAssetBytes
                || buffer.Bytes.LongLength != expectedBytes || buffer.HighestReceivedOffset != expectedBytes)
            {
                error = $"STL bytes are incomplete. buffered={buffer.Bytes.LongLength}, expected={expectedBytes}.";
                return false;
            }

            bytes = (byte[])buffer.Bytes.Clone();
            return true;
        }

        public void ApplyTransferEnd(long teethBytes, long drillBytes)
        {
            TryBuildModel(DentalModelType.Teeth, teethBytes);
            TryBuildModel(DentalModelType.Drill, drillBytes);
            m_Buffers[DentalModelType.Teeth].ManifestTransferActive = false;
            m_Buffers[DentalModelType.Drill].ManifestTransferActive = false;
            ApplyDrillTransformFromMetadata();
        }

        static bool CopyChunk(ModelBuffer buffer, long offset, byte[] data)
        {
            if (offset < 0)
                return false;

            var targetEnd = offset + data.Length;
            if (targetEnd < offset || targetEnd > MaxStlAssetBytes)
            {
                Debug.LogWarning("DentalRobotModelRenderer: STL model is too large for a single Unity byte buffer.");
                return false;
            }

            if (buffer.ManifestTransferActive && targetEnd > buffer.ExpectedBytes)
                return false;

            if (buffer.Bytes.LongLength < targetEnd)
            {
                try
                {
                    Array.Resize(ref buffer.Bytes, (int)targetEnd);
                }
                catch (OutOfMemoryException)
                {
                    return false;
                }
            }

            Buffer.BlockCopy(data, 0, buffer.Bytes, (int)offset, data.Length);

            if (targetEnd > buffer.HighestReceivedOffset)
                buffer.HighestReceivedOffset = targetEnd;
            return true;
        }

        void TryBuildModel(DentalModelType modelType, long expectedBytes)
        {
            if (!m_Buffers.TryGetValue(modelType, out var buffer) || buffer.Bytes.Length == 0)
                return;

            if (expectedBytes > 0 && buffer.Bytes.LongLength < expectedBytes)
            {
                Debug.LogWarning($"DentalRobotModelRenderer: {modelType} STL incomplete. received={buffer.Bytes.LongLength}, expected={expectedBytes}");
                return;
            }

            var bytes = (byte[])buffer.Bytes.Clone();
            if (expectedBytes > 0 && expectedBytes < bytes.Length)
            {
                if (expectedBytes > int.MaxValue)
                {
                    Debug.LogWarning($"DentalRobotModelRenderer: {modelType} STL is too large ({expectedBytes} bytes).");
                    return;
                }

                var trimmed = new byte[(int)expectedBytes];
                System.Array.Copy(bytes, trimmed, trimmed.Length);
                bytes = trimmed;
            }

            if (!DentalStlMeshUtility.TryCreateMesh(bytes, buffer.FileName, out var mesh, false))
            {
                Debug.LogWarning($"DentalRobotModelRenderer: Could not parse {modelType} STL ({buffer.FileName}, {bytes.Length} bytes).");
                return;
            }

            EnsureRoot(XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main);
            if (m_Root == null)
            {
                Destroy(mesh);
                return;
            }

            var target = modelType == DentalModelType.Teeth
                ? EnsureModelObject("Dental Teeth Model", ref m_TeethTransform, GetTeethMaterial())
                : EnsureModelObject("Dental Drill Model", ref m_DrillTransform, GetDrillMaterial());

            var filter = target.GetComponent<MeshFilter>();
            var previousMesh = filter.sharedMesh;
            filter.sharedMesh = mesh;

            if (modelType == DentalModelType.Teeth)
                m_TeethMesh = mesh;
            else
                m_DrillMesh = mesh;

            // Meshes arrive per transfer; drop the replaced one so GPU memory is not retained.
            if (previousMesh != null && previousMesh != mesh && previousMesh != m_TeethMesh && previousMesh != m_DrillMesh)
                Destroy(previousMesh);

            PlaceModel(modelType, target, mesh);
            Debug.Log($"DentalRobotModelRenderer: Displayed {modelType} STL {buffer.FileName}, bytes={bytes.Length}, vertices={mesh.vertexCount}.");
        }

        void AttachToCameraIfNeeded()
        {
            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            if (camera == null)
                return;

            EnsureRoot(camera);
        }

        void EnsureRoot(Camera camera)
        {
            if (camera == null)
                return;

            if (m_Root == null)
            {
                var root = new GameObject("Dental Nav Widget");
                m_Root = root.transform;
                m_Root.gameObject.SetActive(m_WidgetVisible);
            }

            if (m_Camera != camera || m_Root.parent != camera.transform)
            {
                m_Camera = camera;
                m_Root.SetParent(camera.transform, false);
            }

            var layout = DentalDisplayLayoutController.Instance;
            m_Root.localPosition = layout != null ? layout.ModelLocalPositionMeters : m_HeadLockedLocalPosition;
            m_Root.localRotation = Quaternion.identity;
        }

        void ApplyLayoutAndValidity()
        {
            var layout = DentalDisplayLayoutController.Instance;
            if (layout != null)
            {
                m_WidgetVisible = layout.ModelVisible;
                if (m_Root != null)
                {
                    m_Root.localPosition = layout.ModelLocalPositionMeters;
                    if (m_Root.gameObject.activeSelf != m_WidgetVisible)
                        m_Root.gameObject.SetActive(m_WidgetVisible);
                }
            }

            var state = DentalNavigationState.Instance;
            if (state == null)
                return;
            var snapshot = state.Capture(Time.realtimeSinceStartup);
            var valid = snapshot.HasDrillMatrix
                && snapshot.Link == DentalLinkState.Live
                && !snapshot.HideNumbers
                && (!snapshot.HasNavigationFrame || snapshot.FrameValid);
            if (!valid)
            {
                m_HasMetadata = false;
                SetRealtimeObjectsVisible(false);
            }
        }

        void SetRealtimeObjectsVisible(bool visible)
        {
            if (m_DrillTransform != null && m_DrillTransform.gameObject.activeSelf != visible)
                m_DrillTransform.gameObject.SetActive(visible);
            if (m_AxisLine != null && m_AxisLine.gameObject.activeSelf != visible)
                m_AxisLine.gameObject.SetActive(visible);
        }

        Transform EnsureModelObject(string name, ref Transform target, Material material)
        {
            if (target == null)
            {
                var obj = new GameObject(name);
                obj.transform.SetParent(m_Root, false);
                obj.AddComponent<MeshFilter>();
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                target = obj.transform;
            }

            target.GetComponent<MeshRenderer>().material = material;
            return target;
        }

        void PlaceModel(DentalModelType modelType, Transform target, Mesh mesh)
        {
            if (modelType == DentalModelType.Teeth)
            {
                var maxDimension = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
                m_ModelScale = maxDimension > 0f ? m_ModelMaxSizeMeters / maxDimension : 1f;
                m_TeethSceneCenter = mesh.bounds.center;
                if (m_Root != null)
                    m_Root.localScale = Vector3.one * m_ModelScale;
            }

            target.localScale = Vector3.one;
            target.localPosition = -m_TeethSceneCenter;
            target.localRotation = Quaternion.identity;

            if (modelType == DentalModelType.Drill)
                ApplyDrillTransformFromMetadata();
        }

        void ApplyDrillTransformFromMetadata()
        {
            if (!m_HasMetadata || m_DrillTransform == null)
                return;

            // drill_from_teeth is serialized row-by-row. ToUnityMatrix assigns each
            // [row,col] element explicitly, so column 3 is the source translation.
            m_DrillTransform.localPosition = (Vector3)m_DrillMatrix.GetColumn(3) - m_TeethSceneCenter;
            var forward = (Vector3)m_DrillMatrix.GetColumn(2);
            var up = (Vector3)m_DrillMatrix.GetColumn(1);
            if (forward.sqrMagnitude > 0.000001f && up.sqrMagnitude > 0.000001f
                && !float.IsNaN(forward.x + forward.y + forward.z)
                && Vector3.Angle(forward, up) > 0.01f)
                m_DrillTransform.localRotation = Quaternion.LookRotation(forward, up);

            UpdateAxisLine();
        }

        void UpdateAxisLine()
        {
            if (m_Root == null)
                return;

            if (m_AxisLine == null)
            {
                var obj = new GameObject("Drill Axis");
                obj.transform.SetParent(m_Root, false);
                m_AxisLine = obj.AddComponent<LineRenderer>();
                m_AxisLine.useWorldSpace = false;
                m_AxisLine.positionCount = 2;
                m_AxisLine.widthMultiplier = 0.003f;
                m_AxisLine.shadowCastingMode = ShadowCastingMode.Off;
                m_AxisLine.receiveShadows = false;
                var shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader != null)
                    m_AxisLine.material = new Material(shader);
                m_AxisLine.startColor = m_DrillColor;
                m_AxisLine.endColor = m_DrillColor;
            }

            var origin = -m_TeethSceneCenter;
            var direction = m_DrillTransform != null
                ? m_DrillTransform.localRotation * Vector3.forward
                : Vector3.forward;
            if (direction.sqrMagnitude < 0.000001f)
                direction = Vector3.forward;

            m_AxisLine.SetPosition(0, origin);
            m_AxisLine.SetPosition(1, origin + direction.normalized * 0.08f / Mathf.Max(0.0001f, m_ModelScale));
        }

        Material CreateMaterial(Color color, bool transparent)
        {
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            if (transparent)
                SetMaterialTransparent(material);
            return material;
        }

        Material GetTeethMaterial()
        {
            if (m_TeethMaterial == null)
                m_TeethMaterial = CreateMaterial(m_TeethColor, true);

            return m_TeethMaterial;
        }

        Material GetDrillMaterial()
        {
            if (m_DrillMaterial == null)
                m_DrillMaterial = CreateMaterial(m_DrillColor, false);

            return m_DrillMaterial;
        }

        static void SetMaterialTransparent(Material material)
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
    }
}
