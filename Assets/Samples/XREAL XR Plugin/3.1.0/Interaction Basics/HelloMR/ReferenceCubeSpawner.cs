using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Spawns a reference cube in front of the HMD with RGB axis lines at startup.
    /// </summary>
    public class ReferenceCubeSpawner : MonoBehaviour
    {
        [SerializeField]
        bool m_SpawnReferenceCube = false;

        [SerializeField]
        float m_DistanceMeters = 2f;

        [SerializeField]
        float m_CubeSize = 0.3f;

        [SerializeField]
        float m_AxisLength = 0.5f;

        [SerializeField]
        float m_AxisWidth = 0.005f;

        [SerializeField]
        bool m_AutoRotate = true;

        [SerializeField]
        float m_RotationDegreesPerSecond = 45f;

        [SerializeField]
        float m_RotationDegreesPerAxis = 360f;

        [SerializeField]
        bool m_DrawFacePatterns = true;

        [SerializeField]
        int m_PatternTextureSize = 128;

        [SerializeField]
        bool m_SpawnCheckPlane = true;

        [SerializeField]
        string m_CheckPlaneFileName = "check_plane.STL";

        [SerializeField]
        float m_CheckPlaneRightGapMeters = 0.2f;

        [SerializeField]
        float m_CheckPlaneSizeMeters = 0.3f;

        [SerializeField]
        Color m_CheckPlaneColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        [SerializeField]
        float m_MoveStepMeters = 0.05f;

        const float CheckPlaneTransparencyStep = 0.1f;
        const int CheckPlaneColorChannelStep = 25;

        Transform m_CubeRoot;
        Transform m_CheckPlaneRoot;
        Material m_CheckPlaneMaterial;
        int m_CheckPlaneRed;
        int m_CheckPlaneGreen;
        int m_CheckPlaneBlue;
        float m_CheckPlaneAlpha = 1f;

        void Start()
        {
            StartCoroutine(SpawnWhenCameraReady());
        }

        IEnumerator SpawnWhenCameraReady()
        {
            if (!m_SpawnReferenceCube && !m_SpawnCheckPlane)
                yield break;

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
                Debug.LogWarning("ReferenceCubeSpawner: No main camera found.");
                yield break;
            }

            SpawnReferenceCube(camera);
        }

        void SpawnReferenceCube(Camera camera)
        {
            var root = new GameObject("Reference Cube");
            var position = camera.transform.position + camera.transform.forward * m_DistanceMeters;
            root.transform.SetPositionAndRotation(position, Quaternion.identity);

            if (m_SpawnReferenceCube)
            {
                m_CubeRoot = CreateCube(root.transform);
                if (m_DrawFacePatterns)
                    CreateFacePatterns(m_CubeRoot);

                CreateAxis(m_CubeRoot, Vector3.right, Color.red, "X-Axis", m_AxisLength, m_AxisWidth);
                CreateAxis(m_CubeRoot, Vector3.up, Color.green, "Y-Axis", m_AxisLength, m_AxisWidth);
                CreateAxis(m_CubeRoot, Vector3.forward, Color.blue, "Z-Axis", m_AxisLength, m_AxisWidth);

                if (m_AutoRotate)
                    StartCoroutine(RotateAroundLocalAxes(m_CubeRoot));
            }

            if (m_SpawnCheckPlane)
                StartCoroutine(SpawnCheckPlaneModel(root.transform, camera.transform.right));
        }

        public void MoveTargetsByDirection(Vector3 worldDirection)
        {
            if (worldDirection == Vector3.zero)
                return;

            var delta = worldDirection.normalized * m_MoveStepMeters;

            if (m_CubeRoot != null)
                m_CubeRoot.position += delta;

            if (m_CheckPlaneRoot != null)
                m_CheckPlaneRoot.position += delta;
        }

        public bool HasCheckPlane => m_CheckPlaneMaterial != null;

        public void IncreaseCheckPlaneTransparency()
        {
            AdjustCheckPlaneAlpha(-CheckPlaneTransparencyStep);
        }

        public void DecreaseCheckPlaneTransparency()
        {
            AdjustCheckPlaneAlpha(CheckPlaneTransparencyStep);
        }

        public void AdjustCheckPlaneColorChannel(int channel, int delta)
        {
            if (m_CheckPlaneMaterial == null)
                return;

            switch (channel)
            {
                case 0:
                    m_CheckPlaneRed = Mathf.Clamp(m_CheckPlaneRed + delta, 0, 255);
                    break;
                case 1:
                    m_CheckPlaneGreen = Mathf.Clamp(m_CheckPlaneGreen + delta, 0, 255);
                    break;
                case 2:
                    m_CheckPlaneBlue = Mathf.Clamp(m_CheckPlaneBlue + delta, 0, 255);
                    break;
                default:
                    return;
            }

            ApplyCheckPlaneAppearance();
        }

        void AdjustCheckPlaneAlpha(float delta)
        {
            if (m_CheckPlaneMaterial == null)
                return;

            m_CheckPlaneAlpha = Mathf.Clamp01(m_CheckPlaneAlpha + delta);
            ApplyCheckPlaneAppearance();
        }

        void ApplyCheckPlaneAppearance()
        {
            if (m_CheckPlaneMaterial == null)
                return;

            var color = new Color(
                m_CheckPlaneRed / 255f,
                m_CheckPlaneGreen / 255f,
                m_CheckPlaneBlue / 255f,
                m_CheckPlaneAlpha);
            m_CheckPlaneMaterial.color = color;

            if (m_CheckPlaneAlpha >= 0.999f)
                SetMaterialOpaque(m_CheckPlaneMaterial);
            else
                SetMaterialTransparent(m_CheckPlaneMaterial);
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

        static void SetMaterialOpaque(Material material)
        {
            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = -1;
        }

        Transform CreateCube(Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube";
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = Vector3.one * m_CubeSize;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                renderer.material = material;
            }

            return cube.transform;
        }

        IEnumerator SpawnCheckPlaneModel(Transform parent, Vector3 rightDirection)
        {
            byte[] stlBytes = null;
            yield return LoadStreamingAssetBytes(m_CheckPlaneFileName, loadedBytes => stlBytes = loadedBytes);
            if (stlBytes == null || stlBytes.Length == 0)
                yield break;

            if (!DentalStlMeshUtility.TryCreateMesh(stlBytes, m_CheckPlaneFileName, out var mesh))
            {
                Debug.LogWarning($"ReferenceCubeSpawner: Could not parse STL model {m_CheckPlaneFileName}.");
                yield break;
            }

            var maxDimension = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            var modelScale = maxDimension > 0f ? m_CheckPlaneSizeMeters / maxDimension : 1f;

            var modelRoot = new GameObject("Check Plane");
            modelRoot.transform.SetParent(parent, false);
            var cubeHalfWidth = m_SpawnReferenceCube ? m_CubeSize * 0.5f : 0f;
            var rightOffset = cubeHalfWidth + m_CheckPlaneRightGapMeters + m_CheckPlaneSizeMeters * 0.5f;
            modelRoot.transform.position = parent.position + rightDirection.normalized * rightOffset;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.transform.localScale = Vector3.one * modelScale;
            m_CheckPlaneRoot = modelRoot.transform;

            var meshFilter = modelRoot.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = modelRoot.AddComponent<MeshRenderer>();
            m_CheckPlaneMaterial = CreateCheckPlaneMaterial();
            meshRenderer.material = m_CheckPlaneMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            InitializeCheckPlaneAppearanceFromColor(m_CheckPlaneColor);

            if (m_AutoRotate)
                StartCoroutine(RotateAroundLocalAxes(modelRoot.transform));
        }

        IEnumerator LoadStreamingAssetBytes(string fileName, System.Action<byte[]> onLoaded)
        {
            var assetPath = $"{Application.streamingAssetsPath}/{fileName}";
            using (var request = UnityWebRequest.Get(assetPath))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"ReferenceCubeSpawner: Failed to load {assetPath}: {request.error}");
                    yield break;
                }

                onLoaded?.Invoke(request.downloadHandler.data);
            }
        }

        Material CreateCheckPlaneMaterial()
        {
            var material = new Material(Shader.Find("Standard"));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.08f);
            material.color = m_CheckPlaneColor;
            return material;
        }

        void InitializeCheckPlaneAppearanceFromColor(Color color)
        {
            m_CheckPlaneRed = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            m_CheckPlaneGreen = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            m_CheckPlaneBlue = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            m_CheckPlaneAlpha = Mathf.Clamp01(color.a);
            ApplyCheckPlaneAppearance();
        }

        void CreateFacePatterns(Transform cubeTransform)
        {
            CreatePatternFace(cubeTransform, Vector3.forward, Vector3.up, "Front Pattern", 11);
            CreatePatternFace(cubeTransform, Vector3.back, Vector3.up, "Back Pattern", 23);
            CreatePatternFace(cubeTransform, Vector3.right, Vector3.up, "Right Pattern", 37);
            CreatePatternFace(cubeTransform, Vector3.left, Vector3.up, "Left Pattern", 41);
            CreatePatternFace(cubeTransform, Vector3.up, Vector3.back, "Top Pattern", 53);
            CreatePatternFace(cubeTransform, Vector3.down, Vector3.forward, "Bottom Pattern", 67);
        }

        void CreatePatternFace(Transform parent, Vector3 normal, Vector3 up, string faceName, int seed)
        {
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = faceName;
            face.transform.SetParent(parent, false);
            face.transform.localPosition = normal.normalized * 0.501f;
            face.transform.localRotation = Quaternion.LookRotation(normal, up);
            face.transform.localScale = Vector3.one * 0.96f;

            var collider = face.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = face.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = CreatePatternMaterial(seed);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        Material CreatePatternMaterial(int seed)
        {
            var texture = CreatePatternTexture(seed);
            var shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader);
            material.mainTexture = texture;
            material.color = Color.white;
            return material;
        }

        Texture2D CreatePatternTexture(int seed)
        {
            var size = Mathf.Max(32, m_PatternTextureSize);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            var random = new System.Random(seed);
            var palette = new[]
            {
                new Color32(255, 80, 80, 210),
                new Color32(80, 255, 120, 210),
                new Color32(80, 140, 255, 210),
                new Color32(255, 230, 80, 210),
                new Color32(255, 80, 220, 210),
                new Color32(40, 40, 40, 230),
            };

            for (var i = 0; i < 8; i++)
            {
                var centerX = random.Next(size / 8, size * 7 / 8);
                var centerY = random.Next(size / 8, size * 7 / 8);
                var radiusX = random.Next(size / 18, size / 7);
                var radiusY = random.Next(size / 18, size / 7);
                DrawIrregularBlob(pixels, size, centerX, centerY, radiusX, radiusY, palette[random.Next(palette.Length)], random);
            }

            for (var i = 0; i < 7; i++)
            {
                var start = new Vector2Int(random.Next(size), random.Next(size));
                var end = new Vector2Int(random.Next(size), random.Next(size));
                var width = random.Next(2, 6);
                DrawLine(pixels, size, start, end, palette[random.Next(palette.Length)], width);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static void DrawIrregularBlob(Color32[] pixels, int size, int centerX, int centerY, int radiusX, int radiusY, Color32 color, System.Random random)
        {
            var wobbleX = random.Next(-radiusX / 2, radiusX / 2 + 1);
            var wobbleY = random.Next(-radiusY / 2, radiusY / 2 + 1);
            var minX = Mathf.Clamp(centerX - radiusX + wobbleX, 0, size - 1);
            var maxX = Mathf.Clamp(centerX + radiusX + wobbleX, 0, size - 1);
            var minY = Mathf.Clamp(centerY - radiusY + wobbleY, 0, size - 1);
            var maxY = Mathf.Clamp(centerY + radiusY + wobbleY, 0, size - 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var normalizedX = (x - centerX) / (float)Mathf.Max(1, radiusX);
                    var normalizedY = (y - centerY) / (float)Mathf.Max(1, radiusY);
                    var edgeNoise = 0.75f + Mathf.PerlinNoise((x + random.Next(100)) * 0.08f, (y + random.Next(100)) * 0.08f) * 0.55f;
                    if ((normalizedX * normalizedX + normalizedY * normalizedY) < edgeNoise)
                        pixels[y * size + x] = color;
                }
            }
        }

        static void DrawLine(Color32[] pixels, int size, Vector2Int start, Vector2Int end, Color32 color, int width)
        {
            var delta = end - start;
            var steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (steps == 0)
                return;

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(start.x, end.x, t));
                var y = Mathf.RoundToInt(Mathf.Lerp(start.y, end.y, t));
                DrawDisc(pixels, size, x, y, width, color);
            }
        }

        static void DrawDisc(Color32[] pixels, int size, int centerX, int centerY, int radius, Color32 color)
        {
            var minX = Mathf.Clamp(centerX - radius, 0, size - 1);
            var maxX = Mathf.Clamp(centerX + radius, 0, size - 1);
            var minY = Mathf.Clamp(centerY - radius, 0, size - 1);
            var maxY = Mathf.Clamp(centerY + radius, 0, size - 1);
            var radiusSquared = radius * radius;

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    if (dx * dx + dy * dy <= radiusSquared)
                        pixels[y * size + x] = color;
                }
            }
        }

        static void CreateAxis(Transform parent, Vector3 direction, Color color, string axisName, float axisLength, float axisWidth)
        {
            var axisObject = new GameObject(axisName);
            axisObject.transform.SetParent(parent, false);

            var line = axisObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, direction.normalized * axisLength);
            line.startWidth = axisWidth;
            line.endWidth = axisWidth;
            line.startColor = color;
            line.endColor = color;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        IEnumerator RotateAroundLocalAxes(Transform target)
        {
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var axisIndex = 0;

            while (target != null)
            {
                var remainingDegrees = m_RotationDegreesPerAxis;
                var axis = axes[axisIndex];

                while (target != null && remainingDegrees > 0f)
                {
                    var step = Mathf.Min(m_RotationDegreesPerSecond * Time.deltaTime, remainingDegrees);
                    target.Rotate(axis, step, Space.Self);
                    remainingDegrees -= step;
                    yield return null;
                }

                axisIndex = (axisIndex + 1) % axes.Length;
            }
        }
    }
}
