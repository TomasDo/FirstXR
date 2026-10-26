using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalStlMeshBufferTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MeshCreationPreservesSharedTransferBytesAndSourceCoordinates(bool binary)
        {
            var bytes = binary ? CreateBinaryTriangle() : Encoding.ASCII.GetBytes(
                "solid sample\nfacet normal 0 0 1\nouter loop\n" +
                "vertex 10 20 30\nvertex 12 20 30\nvertex 10 22 30\n" +
                "endloop\nendfacet\nendsolid sample\n");
            var original = (byte[])bytes.Clone();
            Mesh sourceMesh = null;
            Mesh centeredMesh = null;
            try
            {
                Assert.That(DentalStlMeshUtility.TryCreateMesh(bytes, "source", out sourceMesh, false), Is.True);
                CollectionAssert.AreEqual(original, bytes, "The renderer now shares its received STL byte buffer.");
                CollectionAssert.AreEqual(new[]
                {
                    new Vector3(10, 20, 30), new Vector3(12, 20, 30), new Vector3(10, 22, 30),
                }, sourceMesh.vertices);

                Assert.That(DentalStlMeshUtility.TryCreateMesh(bytes, "centered", out centeredMesh, true), Is.True);
                CollectionAssert.AreEqual(original, bytes, "Centering must affect the Mesh only.");
                Assert.That(centeredMesh.bounds.center, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                if (sourceMesh != null) Object.DestroyImmediate(sourceMesh);
                if (centeredMesh != null) Object.DestroyImmediate(centeredMesh);
            }
        }

        static byte[] CreateBinaryTriangle()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[80]);
                writer.Write((uint)1);
                foreach (var value in new[] { 0f, 0f, 1f, 10f, 20f, 30f, 12f, 20f, 30f, 10f, 22f, 30f })
                    writer.Write(value);
                writer.Write((ushort)0);
                return stream.ToArray();
            }
        }
    }
}
