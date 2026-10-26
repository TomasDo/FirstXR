using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL.Samples
{
    public static class DentalStlMeshUtility
    {
        const long MaxStlBytes = 512L * 1024 * 1024;
        const int MaxTriangles = 4_000_000;

        public static bool TryCreateMesh(byte[] data, string meshName, out Mesh mesh, bool centerMesh = true)
        {
            return TryCreateBinaryMesh(data, meshName, centerMesh, out mesh) || TryCreateAsciiMesh(data, meshName, centerMesh, out mesh);
        }

        static bool TryCreateBinaryMesh(byte[] data, string meshName, bool centerMesh, out Mesh mesh)
        {
            mesh = null;
            if (data == null || data.Length < 84 || data.Length > MaxStlBytes)
                return false;

            var rawTriangleCount = System.BitConverter.ToUInt32(data, 80);
            if (rawTriangleCount == 0 || rawTriangleCount > MaxTriangles)
                return false;

            var triangleCount = (int)rawTriangleCount;
            var expectedLength = 84L + triangleCount * 50L;
            if (expectedLength != data.Length)
                return false;

            var vertices = new List<Vector3>(triangleCount * 3);
            var normals = new List<Vector3>(triangleCount * 3);
            var triangles = new List<int>(triangleCount * 3);
            var offset = 84;

            for (var i = 0; i < triangleCount; i++)
            {
                var normal = ReadVector3(data, offset);
                offset += 12;

                for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
                {
                    vertices.Add(ReadVector3(data, offset));
                    normals.Add(normal);
                    triangles.Add(vertices.Count - 1);
                    offset += 12;
                }

                offset += 2;
            }

            mesh = BuildMesh(meshName, vertices, triangles, normals, centerMesh);
            return true;
        }

        static bool TryCreateAsciiMesh(byte[] data, string meshName, bool centerMesh, out Mesh mesh)
        {
            mesh = null;
            if (data == null || data.Length == 0)
                return false;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var text = Encoding.ASCII.GetString(data);
            var lines = text.Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("vertex "))
                    continue;

                var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4)
                    continue;

                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    continue;

                vertices.Add(new Vector3(x, y, z));
                triangles.Add(vertices.Count - 1);
            }

            if (vertices.Count < 3 || vertices.Count % 3 != 0)
                return false;

            mesh = BuildMesh(meshName, vertices, triangles, null, centerMesh);
            return true;
        }

        static Vector3 ReadVector3(byte[] data, int offset)
        {
            return new Vector3(
                System.BitConverter.ToSingle(data, offset),
                System.BitConverter.ToSingle(data, offset + 4),
                System.BitConverter.ToSingle(data, offset + 8));
        }

        static Mesh BuildMesh(string meshName, List<Vector3> vertices, List<int> triangles, List<Vector3> normals, bool centerMesh)
        {
            var mesh = new Mesh { name = meshName };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);

            if (normals != null && normals.Count == vertices.Count)
                mesh.SetNormals(normals);
            else
                RecalculateNormalsSafe(mesh);

            mesh.RecalculateBounds();
            if (centerMesh)
                CenterMesh(mesh);
            return mesh;
        }

        // STL files frequently contain zero or NaN facet normals; Unity rejects those silently
        // and leaves the mesh with degenerate shading, so fall back to computed normals.
        static void RecalculateNormalsSafe(Mesh mesh)
        {
            try
            {
                mesh.RecalculateNormals();
            }
            catch (System.ArgumentException)
            {
                var fallback = new Vector3[mesh.vertexCount];
                for (var i = 0; i < fallback.Length; i++)
                    fallback[i] = Vector3.up;
                mesh.SetNormals(fallback);
            }
        }

        static void CenterMesh(Mesh mesh)
        {
            var center = mesh.bounds.center;
            var vertices = mesh.vertices;

            for (var i = 0; i < vertices.Length; i++)
                vertices[i] -= center;

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
    }
}
