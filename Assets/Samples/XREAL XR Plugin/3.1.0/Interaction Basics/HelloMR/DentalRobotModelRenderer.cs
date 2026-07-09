using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    public class DentalRobotModelRenderer : MonoBehaviour
    {
        public enum DentalModelType
        {
            Unknown = 0,
            Teeth = 1,
            Drill = 2,
        }

        class ModelBuffer
        {
            public readonly List<byte> Bytes = new List<byte>(1024 * 1024);
            public string FileName = string.Empty;
            public long HighestReceivedOffset;

            public void Clear()
            {
                Bytes.Clear();
                FileName = string.Empty;
                HighestReceivedOffset = 0;
            }
        }

        [SerializeField]
        float m_DistanceMeters = 1.6f;

        [SerializeField]
        float m_VerticalOffsetMeters = -0.15f;

        [SerializeField]
        float m_ModelMaxSizeMeters = 0.45f;

        [SerializeField]
        Color m_TeethColor = new Color(0.85f, 0.85f, 0.82f, 0.9f);

        [SerializeField]
        Color m_DrillColor = new Color(1f, 0.84f, 0.1f, 1f);

        static DentalRobotModelRenderer s_Instance;

        readonly Dictionary<DentalModelType, ModelBuffer> m_Buffers = new Dictionary<DentalModelType, ModelBuffer>();
        readonly double[] m_DrillFromTeeth = new double[16];

        Transform m_Root;
        Transform m_TeethTransform;
        Transform m_DrillTransform;
        Vector3 m_TeethSceneCenter;
        float m_ModelScale = 1f;
        Material m_TeethMaterial;
        Material m_DrillMaterial;
        bool m_HasMetadata;
        bool m_RootAnchored;

        public static DentalRobotModelRenderer Instance => s_Instance;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            m_Buffers[DentalModelType.Teeth] = new ModelBuffer();
            m_Buffers[DentalModelType.Drill] = new ModelBuffer();
        }

        void Start()
        {
            StartCoroutine(CreateRootWhenCameraReady());
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            if (m_TeethMaterial != null)
                Destroy(m_TeethMaterial);

            if (m_DrillMaterial != null)
                Destroy(m_DrillMaterial);
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

        public void ApplyMetadata(IList<double> drillFromTeeth)
        {
            m_HasMetadata = drillFromTeeth != null && drillFromTeeth.Count >= 16;
            for (var i = 0; i < m_DrillFromTeeth.Length; i++)
                m_DrillFromTeeth[i] = m_HasMetadata ? drillFromTeeth[i] : 0d;

            ApplyDrillTransformFromMetadata();
        }

        public void ApplyStlChunk(DentalModelType modelType, string filename, long offset, byte[] data)
        {
            if (modelType != DentalModelType.Teeth && modelType != DentalModelType.Drill)
                return;

            if (data == null || data.Length == 0)
                return;

            var buffer = m_Buffers[modelType];
            if (offset == 0 && buffer.Bytes.Count > 0)
                buffer.Clear();

            buffer.FileName = string.IsNullOrEmpty(filename) ? modelType.ToString() + ".stl" : filename;
            CopyChunk(buffer, offset, data);
        }

        public void ApplyTransferEnd(long teethBytes, long drillBytes)
        {
            TryBuildModel(DentalModelType.Teeth, teethBytes);
            TryBuildModel(DentalModelType.Drill, drillBytes);
            ApplyDrillTransformFromMetadata();
        }

        static void CopyChunk(ModelBuffer buffer, long offset, byte[] data)
        {
            if (offset < 0)
                offset = 0;

            var targetEnd = offset + data.Length;
            if (targetEnd > int.MaxValue)
            {
                Debug.LogWarning("DentalRobotModelRenderer: STL model is too large for a single Unity byte buffer.");
                return;
            }

            while (buffer.Bytes.Count < targetEnd)
                buffer.Bytes.Add(0);

            for (var i = 0; i < data.Length; i++)
                buffer.Bytes[(int)offset + i] = data[i];

            if (targetEnd > buffer.HighestReceivedOffset)
                buffer.HighestReceivedOffset = targetEnd;
        }

        void TryBuildModel(DentalModelType modelType, long expectedBytes)
        {
            if (!m_Buffers.TryGetValue(modelType, out var buffer) || buffer.Bytes.Count == 0)
                return;

            if (expectedBytes > 0 && buffer.Bytes.Count < expectedBytes)
            {
                Debug.LogWarning($"DentalRobotModelRenderer: {modelType} STL incomplete. received={buffer.Bytes.Count}, expected={expectedBytes}");
                return;
            }

            var bytes = buffer.Bytes.ToArray();
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
                return;

            var target = modelType == DentalModelType.Teeth
                ? EnsureModelObject("Dental Teeth Model", ref m_TeethTransform, GetTeethMaterial())
                : EnsureModelObject("Dental Drill Model", ref m_DrillTransform, GetDrillMaterial());

            var filter = target.GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            PlaceModel(modelType, target, mesh);
            Debug.Log($"DentalRobotModelRenderer: Displayed {modelType} STL {buffer.FileName}, bytes={bytes.Length}, vertices={mesh.vertexCount}.");
        }

        void EnsureRoot(Camera camera)
        {
            if (m_Root != null || camera == null)
                return;

            var root = new GameObject("Dental Robot Models");
            var position = camera.transform.position
                + camera.transform.forward * m_DistanceMeters
                + camera.transform.up * m_VerticalOffsetMeters;
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(camera.transform.forward, camera.transform.up));
            m_Root = root.transform;
        }

        Transform EnsureModelObject(string name, ref Transform target, Material material)
        {
            if (target == null)
            {
                var obj = new GameObject(name);
                obj.transform.SetParent(m_Root, false);
                obj.AddComponent<MeshFilter>();
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
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

                AnchorRootInFrontOfCamera();
            }

            target.localScale = Vector3.one;
            target.localPosition = -m_TeethSceneCenter;
            target.localRotation = Quaternion.identity;

            if (modelType == DentalModelType.Drill)
                ApplyDrillTransformFromMetadata();
        }

        /// <summary>
        /// Locks the model root at a fixed world-space pose in front of the camera the moment the
        /// teeth model is placed. Under 6DOF head tracking this keeps the teeth spatially anchored;
        /// the drill is then positioned relative to the teeth via the drill_from_teeth matrix.
        /// </summary>
        void AnchorRootInFrontOfCamera()
        {
            if (m_RootAnchored || m_Root == null)
                return;

            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            if (camera != null)
            {
                var position = camera.transform.position
                    + camera.transform.forward * m_DistanceMeters
                    + camera.transform.up * m_VerticalOffsetMeters;
                m_Root.SetPositionAndRotation(position, Quaternion.LookRotation(camera.transform.forward, camera.transform.up));
            }

            m_RootAnchored = true;
        }

        void ApplyDrillTransformFromMetadata()
        {
            if (!m_HasMetadata || m_DrillTransform == null)
                return;

            var matrix = new Matrix4x4();
            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                    matrix[row, col] = (float)m_DrillFromTeeth[row * 4 + col];
            }

            m_DrillTransform.localPosition = (Vector3)matrix.GetColumn(3) - m_TeethSceneCenter;
            var forward = (Vector3)matrix.GetColumn(2);
            var up = (Vector3)matrix.GetColumn(1);
            if (forward.sqrMagnitude > 0.000001f && up.sqrMagnitude > 0.000001f)
                m_DrillTransform.localRotation = Quaternion.LookRotation(forward, up);
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
