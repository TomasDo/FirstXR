using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Spawns a reference cube in front of the HMD with RGB axis lines at startup.
    /// </summary>
    public class ReferenceCubeSpawner : MonoBehaviour
    {
        [SerializeField]
        float m_DistanceMeters = 2f;

        [SerializeField]
        float m_CubeSize = 0.3f;

        [SerializeField]
        float m_AxisLength = 0.5f;

        [SerializeField]
        float m_AxisWidth = 0.005f;

        void Start()
        {
            StartCoroutine(SpawnWhenCameraReady());
        }

        IEnumerator SpawnWhenCameraReady()
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

            var cubeTransform = CreateCube(root.transform);
            CreateAxis(cubeTransform, Vector3.right, Color.red, "X-Axis", m_AxisLength, m_AxisWidth);
            CreateAxis(cubeTransform, Vector3.up, Color.green, "Y-Axis", m_AxisLength, m_AxisWidth);
            CreateAxis(cubeTransform, Vector3.forward, Color.blue, "Z-Axis", m_AxisLength, m_AxisWidth);
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
    }
}
