using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalDicomTests
    {
        string m_TemporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            DestroyRuntimeSingletons();
            m_TemporaryDirectory = Path.Combine(Path.GetTempPath(), "DentalDicomTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_TemporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyRuntimeSingletons();
            if (Directory.Exists(m_TemporaryDirectory))
                Directory.Delete(m_TemporaryDirectory, true);
        }

        [Test]
        public void ChunkStore_ResumesOutOfOrderAndCompletesOnlyAfterHashMatches()
        {
            var bytes = DicomFixture.Create(positionZ: 0, rawPixels: new[] { -1024, 0, 100, 2047 });
            var descriptor = new DicomTransferFileDescriptor("series/instance-1", "slice001.dcm", bytes.Length, Sha256(bytes));
            var firstStore = new DentalDicomTransferStore(m_TemporaryDirectory);
            Assert.That(firstStore.BeginAsset(descriptor, out var error), Is.True, error);

            var split = bytes.Length / 2;
            Assert.That(firstStore.WriteChunk(descriptor.AssetId, split, Slice(bytes, split, bytes.Length - split), out error), Is.True, error);
            Assert.That(firstStore.TryCompleteAsset(descriptor.AssetId, out _, out error), Is.False);
            StringAssert.Contains("incomplete", error.ToLowerInvariant());

            var resumedStore = new DentalDicomTransferStore(m_TemporaryDirectory);
            Assert.That(resumedStore.BeginAsset(descriptor, out error), Is.True, error);
            Assert.That(resumedStore.TryGetProgress(descriptor.AssetId, out var progress), Is.True);
            Assert.That(progress.CoveredBytes, Is.EqualTo(bytes.Length - split));
            Assert.That(progress.NextMissingOffset, Is.EqualTo(0));
            Assert.That(resumedStore.WriteChunk(descriptor.AssetId, 0, Slice(bytes, 0, split), out error), Is.True, error);
            Assert.That(resumedStore.TryCompleteAsset(descriptor.AssetId, out var completedPath, out error), Is.True, error);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(completedPath));
        }

        [Test]
        public void ChunkStore_RejectsConflictingRetryAndWrongDigest()
        {
            var bytes = DicomFixture.Create(positionZ: 0, rawPixels: new[] { 1, 2, 3, 4 });
            var wrongDigest = new string('0', 64);
            var descriptor = new DicomTransferFileDescriptor("instance-2", "slice002.dcm", bytes.Length, wrongDigest);
            var store = new DentalDicomTransferStore(m_TemporaryDirectory);
            Assert.That(store.BeginAsset(descriptor, out var error), Is.True, error);
            Assert.That(store.WriteChunk(descriptor.AssetId, 0, bytes, out error), Is.True, error);

            var conflicting = Slice(bytes, 0, 16);
            conflicting[4] ^= 0xff;
            Assert.That(store.WriteChunk(descriptor.AssetId, 0, conflicting, out error), Is.False);
            StringAssert.Contains("conflicts", error);
            Assert.That(store.TryCompleteAsset(descriptor.AssetId, out _, out error), Is.False);
            StringAssert.Contains("SHA-256 mismatch", error);
        }

        [Test]
        public void ChunkStore_IsolatesSameAssetIdAcrossTransferNamespaces()
        {
            var firstBytes = DicomFixture.Create(positionZ: 0, rawPixels: new[] { 1, 2, 3, 4 });
            var secondBytes = DicomFixture.Create(positionZ: 1, rawPixels: new[] { 5, 6, 7, 8 });
            var first = new DicomTransferFileDescriptor(
                "transfer-a", "shared-asset", "slice.dcm", firstBytes.Length, Sha256(firstBytes));
            var second = new DicomTransferFileDescriptor(
                "transfer-b", "shared-asset", "slice.dcm", secondBytes.Length, Sha256(secondBytes));
            var store = new DentalDicomTransferStore(m_TemporaryDirectory);

            Assert.That(store.BeginAsset(first, out var error), Is.True, error);
            Assert.That(store.BeginAsset(second, out error), Is.True, error);
            Assert.That(store.WriteChunk(first.TransferNamespace, first.AssetId, 0, firstBytes, out error), Is.True, error);
            Assert.That(store.WriteChunk(second.TransferNamespace, second.AssetId, 0, secondBytes, out error), Is.True, error);
            Assert.That(store.TryCompleteAsset(
                first.TransferNamespace, first.AssetId, out var firstPath, out error), Is.True, error);
            Assert.That(store.TryCompleteAsset(
                second.TransferNamespace, second.AssetId, out var secondPath, out error), Is.True, error);

            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(firstPath));
            CollectionAssert.AreEqual(secondBytes, File.ReadAllBytes(secondPath));
        }

        [Test]
        public void VolumeService_RejectsChunksFromStaleTransferGeneration()
        {
            var firstBytes = DicomFixture.Create(positionZ: 0, rawPixels: new[] { 1, 2, 3, 4 });
            var secondBytes = DicomFixture.Create(positionZ: 1, rawPixels: new[] { 5, 6, 7, 8 });
            var serviceObject = new GameObject("CT transfer generation test");
            var service = serviceObject.AddComponent<DentalCtVolumeService>();
            InvokeLifecycle(service, "Awake");
            service.Configure(Path.Combine(m_TemporaryDirectory, "generation-store"), new ExplicitVrLittleEndianDicomDecoder());

            var firstGeneration = service.ActivateTransferScope("transfer-a");
            var first = new DicomTransferFileDescriptor(
                "transfer-a", "shared-asset", "slice.dcm", firstBytes.Length, Sha256(firstBytes));
            Assert.That(service.BeginAsset(firstGeneration, first.TransferNamespace, first, out var error), Is.True, error);
            var firstWrite = service.WriteAssetChunkBackground(
                firstGeneration, first.TransferNamespace, first.AssetId, 0, firstBytes);
            Assert.That(firstWrite.Success, Is.True, firstWrite.Error);

            var secondGeneration = service.ActivateTransferScope("transfer-b");
            Assert.That(secondGeneration, Is.Not.EqualTo(firstGeneration));
            var staleWrite = service.WriteAssetChunkBackground(
                firstGeneration, first.TransferNamespace, first.AssetId, 0, firstBytes);
            Assert.That(staleWrite.Success, Is.False);
            StringAssert.Contains("stale", staleWrite.Error);
            var staleCompletion = service.CompleteAssetAsync(
                firstGeneration, first.TransferNamespace, first.AssetId, null).GetAwaiter().GetResult();
            Assert.That(staleCompletion.Success, Is.False);
            StringAssert.Contains("stale", staleCompletion.Error);

            var second = new DicomTransferFileDescriptor(
                "transfer-b", "shared-asset", "slice.dcm", secondBytes.Length, Sha256(secondBytes));
            Assert.That(service.BeginAsset(secondGeneration, second.TransferNamespace, second, out error), Is.True, error);
            var currentWrite = service.WriteAssetChunkBackground(
                secondGeneration, second.TransferNamespace, second.AssetId, 0, secondBytes);
            Assert.That(currentWrite.Success, Is.True, currentWrite.Error);
            Assert.That(service.CompleteAsset(secondGeneration, second.TransferNamespace, second.AssetId, out error), Is.True, error);
            Assert.That(service.CommitVolume(out error), Is.True, error);
            Assert.That(service.HasVolume, Is.True);

            service.InvalidateTransferScopeAndClearVolume();
            Assert.That(service.HasVolume, Is.False);
            var invalidatedWrite = service.WriteAssetChunkBackground(
                secondGeneration, second.TransferNamespace, second.AssetId, 0, secondBytes);
            Assert.That(invalidatedWrite.Success, Is.False);
            StringAssert.Contains("stale", invalidatedWrite.Error);
        }

        [Test]
        public void Decoder_ReadsSignedStoredBitsRescaleAndGeometry()
        {
            var bytes = DicomFixture.Create(positionZ: 12.5, rawPixels: new[] { -1024, 0, 100, 2047 }, slope: 2, intercept: -100);
            var decoder = new ExplicitVrLittleEndianDicomDecoder();
            using (var stream = new MemoryStream(bytes, false))
            {
                Assert.That(decoder.TryDecode(stream, "fixture.dcm", out var decoded, out var error), Is.True, error);
                Assert.That(decoded.SopInstanceUid, Is.EqualTo(DicomFixture.SopInstanceUid));
                Assert.That(decoded.SeriesInstanceUid, Is.EqualTo(DicomFixture.SeriesInstanceUid));
                Assert.That(decoded.Frames.Count, Is.EqualTo(1));
                var frame = decoded.Frames[0];
                CollectionAssert.AreEqual(new[] { -1024, 0, 100, 2047 }, frame.RawPixels);
                Assert.That(frame.GetRescaledPixel(0, 0), Is.EqualTo(-2148));
                Assert.That(frame.GetRescaledPixel(1, 1), Is.EqualTo(3994));
                Assert.That(frame.Geometry.ImagePositionPatient.Z, Is.EqualTo(12.5).Within(1e-9));
                Assert.That(frame.Geometry.PixelCenterPatient(1, 1).X, Is.EqualTo(0.5).Within(1e-9));
                Assert.That(frame.Geometry.PixelCenterPatient(1, 1).Y, Is.EqualTo(0.75).Within(1e-9));
                Assert.That(frame.Geometry.PixelCenterPatient(1, 1).Z, Is.EqualTo(12.5).Within(1e-9));
            }
        }

        [Test]
        public void Decoder_RejectsUnsupportedTransferSyntax()
        {
            var bytes = DicomFixture.Create(positionZ: 0, rawPixels: new[] { 1, 2, 3, 4 }, transferSyntax: "1.2.840.10008.1.2");
            var decoder = new ExplicitVrLittleEndianDicomDecoder();
            using (var stream = new MemoryStream(bytes, false))
            {
                Assert.That(decoder.TryDecode(stream, "implicit.dcm", out _, out var error), Is.False);
                StringAssert.Contains("Unsupported DICOM transfer syntax", error);
            }
        }

        [Test]
        public void Decoder_ReadsAllFramesFromUncompressedMultiFrameInstance()
        {
            var bytes = DicomFixture.Create(
                positionZ: 5,
                rawPixels: new[] { 1, 2, 3, 4, 101, 102, 103, 104 },
                sliceSpacing: 1.5);
            var decoder = new ExplicitVrLittleEndianDicomDecoder();
            using (var stream = new MemoryStream(bytes, false))
            {
                Assert.That(decoder.TryDecode(stream, "multiframe.dcm", out var decoded, out var error), Is.True, error);
                Assert.That(decoded.Frames.Count, Is.EqualTo(2));
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, decoded.Frames[0].RawPixels);
                CollectionAssert.AreEqual(new[] { 101, 102, 103, 104 }, decoded.Frames[1].RawPixels);
                Assert.That(decoded.Frames[1].Geometry.ImagePositionPatient.Z, Is.EqualTo(6.5).Within(1e-9));
            }
        }

        [Test]
        public void AssetExpectation_ValidatesManifestSopAndFrameCount()
        {
            var decoder = new ExplicitVrLittleEndianDicomDecoder();
            var bytes = DicomFixture.Create(
                positionZ: 5,
                rawPixels: new[] { 1, 2, 3, 4, 101, 102, 103, 104 },
                sliceSpacing: 1.5);
            using (var stream = new MemoryStream(bytes, false))
            {
                Assert.That(decoder.TryDecode(stream, "two-frame.dcm", out var decoded, out var error), Is.True, error);
                Assert.That(new DicomAssetExpectation(DicomFixture.SopInstanceUid, 2, 4)
                    .TryValidate(decoded, out error), Is.True, error);
                Assert.That(new DicomAssetExpectation("1.2.3.invalid", 2, 4)
                    .TryValidate(decoded, out error), Is.False);
                StringAssert.Contains("SOPInstanceUID mismatch", error);
                Assert.That(new DicomAssetExpectation(DicomFixture.SopInstanceUid, 1, 4)
                    .TryValidate(decoded, out error), Is.False);
                StringAssert.Contains("frame count mismatch", error);
            }
        }

        [Test]
        public void Volume_SortsPhysicalSlicesAndSamplesPatientCoordinates()
        {
            var lower = Decode(DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            var upper = Decode(DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid));
            Assert.That(DicomVolume.TryCreate(new[] { upper, lower }, out var volume, out var error), Is.True, error);

            Assert.That(volume.Depth, Is.EqualTo(2));
            Assert.That(volume.SliceSpacingMm, Is.EqualTo(2).Within(1e-9));
            Assert.That(volume.OriginPatient.Z, Is.EqualTo(10).Within(1e-9));
            var midpoint = volume.VoxelCenterPatient(0.5, 0.5, 0.5);
            Assert.That(volume.TrySamplePatientTrilinear(midpoint, out var value), Is.True);
            Assert.That(value, Is.EqualTo(65).Within(1e-5));
        }

        [Test]
        public void Volume_RejectsShiftedSliceOriginsItCannotRepresent()
        {
            var lower = Decode(DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            var upperSource = Decode(DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid));
            var shiftedGeometry = new DicomImageGeometry(
                new DicomVector3d(0.2, 0, 12),
                upperSource.Geometry.ColumnIndexDirection,
                upperSource.Geometry.RowIndexDirection,
                upperSource.Geometry.RowSpacingMm,
                upperSource.Geometry.ColumnSpacingMm,
                upperSource.Geometry.SuggestedSliceSpacingMm);
            var shifted = new DicomImageFrame(
                upperSource.SopInstanceUid,
                upperSource.FrameNumber,
                upperSource.Rows,
                upperSource.Columns,
                upperSource.RawPixels,
                upperSource.RescaleSlope,
                upperSource.RescaleIntercept,
                upperSource.WindowCenter,
                upperSource.WindowWidth,
                upperSource.IsMonochrome1,
                shiftedGeometry,
                upperSource.FrameOfReferenceUid,
                upperSource.SeriesInstanceUid);

            Assert.That(DicomVolume.TryCreate(new[] { lower, shifted }, out _, out var error), Is.False);
            StringAssert.Contains("shifted slice origins", error);
        }

        [Test]
        public void Volume_RejectsDuplicateSopFramesAndMixedSeries()
        {
            var lower = Decode(DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            var duplicateSop = Decode(DicomFixture.Create(positionZ: 12, rawPixels: new[] { 100, 110, 120, 130 }));
            Assert.That(DicomVolume.TryCreate(new[] { lower, duplicateSop }, out _, out var error), Is.False);
            StringAssert.Contains("duplicate", error.ToLowerInvariant());

            var otherSeries = Decode(DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid,
                seriesInstanceUid: DicomFixture.SecondSeriesInstanceUid));
            Assert.That(DicomVolume.TryCreate(new[] { lower, otherSeries }, out _, out error), Is.False);
            StringAssert.Contains("SeriesInstanceUID", error);
        }

        [Test]
        public void SliceCache_RendersWindowedPixelsAndReturnsCachedBuffer()
        {
            var frame = Decode(DicomFixture.Create(positionZ: 0, rawPixels: new[] { 0, 100, 200, 300 }, windowCenter: 150, windowWidth: 300));
            Assert.That(DicomVolume.TryCreate(new[] { frame }, out var volume, out var error), Is.True, error);
            var request = DicomSliceRequest.Native(volume, 0);
            var cache = new DicomCpuSliceCache(2);
            var first = cache.GetOrCreate(volume, request);
            var second = cache.GetOrCreate(volume, request);

            Assert.That(second, Is.SameAs(first));
            Assert.That(first.Length, Is.EqualTo(4));
            Assert.That(first[0], Is.LessThan(first[1]));
            Assert.That(first[1], Is.LessThan(first[2]));
            Assert.That(first[2], Is.LessThan(first[3]));
        }

        [Test]
        public void PatientPlane_UsesBuccalUpAndMesialLeftContract()
        {
            var lower = Decode(DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            var upper = Decode(DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid));
            Assert.That(DicomVolume.TryCreate(new[] { lower, upper }, out var volume, out var error), Is.True, error);

            Assert.That(DicomPatientPlaneFactory.TryCreate(
                volume,
                new DicomVector3d(0, 0, 10),
                new DicomVector3d(0, 0, 1),
                new DicomVector3d(0, 1, 0),
                0,
                128,
                out var plane,
                out error), Is.True, error);

            // normal x buccalUp = -X is mesial/screen-left, so pixels advance +X to screen-right.
            Assert.That(plane.Request.HorizontalDirection.X, Is.EqualTo(1).Within(1e-9));
            Assert.That(plane.Request.VerticalDirection.Y, Is.EqualTo(-1).Within(1e-9));
            Assert.That(plane.Request.TopLeftPatient.Y, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(plane.StepMm, Is.EqualTo(0.5).Within(1e-9));

            Assert.That(DicomPatientPlaneFactory.TryCreate(
                volume,
                new DicomVector3d(0, 0, 10),
                new DicomVector3d(0, 0, 1),
                new DicomVector3d(0, 1, 0),
                20,
                128,
                out _,
                out error), Is.False);
            StringAssert.Contains("does not intersect", error);
        }

        [Test]
        public void TextureCache_CreatesReusableUnityTexture()
        {
            var frame = Decode(DicomFixture.Create(positionZ: 0, rawPixels: new[] { 0, 100, 200, 300 }, windowCenter: 150, windowWidth: 300));
            Assert.That(DicomVolume.TryCreate(new[] { frame }, out var volume, out var error), Is.True, error);
            var request = DicomSliceRequest.Native(volume, 0);
            var cache = new DicomSliceTextureCache(2);
            try
            {
                var first = cache.GetOrCreate(volume, request);
                var second = cache.GetOrCreate(volume, request);
                Assert.That(first, Is.SameAs(second));
                Assert.That(first.width, Is.EqualTo(2));
                Assert.That(first.height, Is.EqualTo(2));
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void LatestSliceQueue_KeepsOneWorkerAndOnlyDeliversNewestRequest()
        {
            var lower = Decode(DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            var upper = Decode(DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid));
            Assert.That(DicomVolume.TryCreate(new[] { lower, upper }, out var volume, out var error), Is.True, error);
            var renderer = new BlockingSliceRenderer();

            using (var queue = new DicomLatestSliceRenderQueue(2, renderer))
            {
                var firstVersion = queue.Request(volume, DicomSliceRequest.Native(volume, 0));
                Assert.That(renderer.FirstStarted.Wait(TimeSpan.FromSeconds(2)), Is.True, "First render did not start.");
                var secondRequest = DicomSliceRequest.Native(volume, 1);
                var secondVersion = queue.Request(volume, secondRequest);
                renderer.ReleaseFirst.Set();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                var hasResult = false;
                var result = default(DicomSlicePixelResult);
                while (DateTime.UtcNow < deadline && !(hasResult = queue.TryTakeLatest(out result)))
                    Thread.Sleep(2);

                Assert.That(hasResult, Is.True, "Newest render did not complete.");
                Assert.That(secondVersion, Is.GreaterThan(firstVersion));
                Assert.That(result.RequestVersion, Is.EqualTo(secondVersion));
                Assert.That(result.Request, Is.EqualTo(secondRequest));
                Assert.That(result.Pixels[0], Is.EqualTo(2));
                Assert.That(renderer.MaximumConcurrentCalls, Is.EqualTo(1));
                Assert.That(queue.TryTakeLatest(out _), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator AsyncDecode_DiscardsResultAfterNavigationContextChanges()
        {
            var bytes = DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 });
            var descriptor = new DicomTransferFileDescriptor("stale-instance", "stale.dcm", bytes.Length, Sha256(bytes));
            var decoder = new BlockingDicomDecoder();
            var serviceObject = new GameObject("CT stale decode test");
            var service = serviceObject.AddComponent<DentalCtVolumeService>();
            InvokeLifecycle(service, "Awake");
            service.Configure(Path.Combine(m_TemporaryDirectory, "stale-store"), decoder);
            Assert.That(service.BeginAsset(descriptor, out var error), Is.True, error);
            Assert.That(service.WriteAssetChunk(descriptor.AssetId, 0, bytes, out error), Is.True, error);

            var decodeTask = service.CompleteAssetAsync(descriptor.AssetId);
            try
            {
                Assert.That(decoder.Started.Wait(TimeSpan.FromSeconds(2)), Is.True, "Decode worker did not start.");
                var state = DentalNavigationState.EnsureInstance();
                Assert.That(state.ApplyNavigationContext(new DentalNavigationContext(
                    "new-session", "case", "dataset", "new-ct", "plan", "tooth", "tool", "step", 1,
                    Vector3.zero, Vector3.forward, 10f, Vector3.up, Vector3.left,
                    DicomFixture.FrameOfReferenceUid, false, Matrix4x4.identity)), Is.True);
                decoder.Release.Set();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!decodeTask.IsCompleted && DateTime.UtcNow < deadline)
                    yield return null;
                Assert.That(decodeTask.IsCompleted, Is.True, "Decode task did not complete.");
                var result = decodeTask.GetAwaiter().GetResult();
                Assert.That(result.Success, Is.False);
                StringAssert.Contains("discarded", result.Error);
                Assert.That(service.CommitVolume(out error), Is.False);
                StringAssert.Contains("No DICOM", error);
            }
            finally
            {
                decoder.Release.Set();
            }
        }

        [Test]
        public void SliceCoordinator_AppliesDefaultAndOnlyReappliesNewRemoteControlVersions()
        {
            var firstPath = Path.Combine(m_TemporaryDirectory, "first.dcm");
            var secondPath = Path.Combine(m_TemporaryDirectory, "second.dcm");
            File.WriteAllBytes(firstPath, DicomFixture.Create(positionZ: 10, rawPixels: new[] { 0, 10, 20, 30 }));
            File.WriteAllBytes(secondPath, DicomFixture.Create(
                positionZ: 12,
                rawPixels: new[] { 100, 110, 120, 130 },
                sopInstanceUid: DicomFixture.SecondSopInstanceUid));

            var serviceObject = new GameObject("CT coordinator test");
            var service = serviceObject.AddComponent<DentalCtVolumeService>();
            // EditMode does not run ordinary MonoBehaviour lifecycle callbacks. Invoke the same
            // initialization sequence that Unity runs when the service starts in the player.
            InvokeLifecycle(service, "Awake");
            var coordinator = serviceObject.GetComponent<DentalCtSliceCoordinator>();
            Assert.That(coordinator, Is.Not.Null);
            InvokeLifecycle(coordinator, "Awake");
            InvokeLifecycle(coordinator, "OnEnable");
            service.Configure(Path.Combine(m_TemporaryDirectory, "store"), new ExplicitVrLittleEndianDicomDecoder());
            var state = DentalNavigationState.EnsureInstance();
            var context = new DentalNavigationContext(
                "session", "case", "dataset", "ct", "plan", "tooth", "tool", "step", 1,
                new Vector3(0.5f, 0.5f, 10f), Vector3.forward, 10f,
                Vector3.up, Vector3.left,
                DicomFixture.FrameOfReferenceUid, false, Matrix4x4.identity);

            Assert.That(state.ApplyNavigationContext(context), Is.True);
            Assert.That(service.LoadVerifiedDicomFile(firstPath, out var error), Is.True, error);
            Assert.That(service.LoadVerifiedDicomFile(secondPath, out error), Is.True, error);
            Assert.That(service.CommitVolume(out error), Is.True, error);
            Assert.That(service.UsesPatientPlane, Is.True, service.StatusMessage);
            Assert.That(service.SliceOffsetMm, Is.EqualTo(0).Within(1e-5));
            WaitForTexture(service);

            var remoteIndex = new DentalSliceState(
                "session", 1, 1, true, DentalControlSource.NavigationSoftware,
                false, "ct", string.Empty, default, default, default, 0, 0, string.Empty);
            Assert.That(state.ApplySliceState(remoteIndex), Is.True);
            Assert.That(service.UsesPatientPlane, Is.False);
            Assert.That(service.SliceIndex, Is.EqualTo(0));

            // A local change raises SliceChanged, but must not recursively replay remote version 1.
            service.SetSliceIndex(1);
            Assert.That(service.SliceIndex, Is.EqualTo(1));

            var remotePlane = new DentalSliceState(
                "session", 1, 2, true, DentalControlSource.NavigationSoftware,
                true, "ct", DicomFixture.FrameOfReferenceUid,
                new Vector3(0.5f, 0.5f, 10f), Vector3.forward, Vector3.up,
                2f, 0, string.Empty);
            Assert.That(state.ApplySliceState(remotePlane), Is.True);
            Assert.That(service.UsesPatientPlane, Is.True, service.StatusMessage);
            Assert.That(service.SliceOffsetMm, Is.EqualTo(2).Within(1e-5));
            WaitForTexture(service);

            var outOfRange = new DentalSliceState(
                "session", 1, 3, true, DentalControlSource.NavigationSoftware,
                false, "ct", string.Empty, default, default, default, 0, 99, string.Empty);
            Assert.That(state.CanApplySliceState(outOfRange), Is.True);
            Assert.That(coordinator.TryExecuteControl(
                outOfRange,
                out var disposition,
                out var executionError), Is.False);
            Assert.That(disposition, Is.EqualTo(DentalSliceExecutionDisposition.Rejected));
            StringAssert.Contains("范围", executionError);
            Assert.That(state.Capture(Time.realtimeSinceStartup).SliceState.ControlVersion, Is.EqualTo(2));
        }

        static void DestroyRuntimeSingletons()
        {
            if (DentalCtVolumeService.Instance != null)
                UnityEngine.Object.DestroyImmediate(DentalCtVolumeService.Instance.gameObject);
            if (DentalNavigationState.Instance != null)
                UnityEngine.Object.DestroyImmediate(DentalNavigationState.Instance.gameObject);
        }

        static void InvokeLifecycle(MonoBehaviour behaviour, string methodName)
        {
            var method = behaviour.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing lifecycle method {methodName}");
            method.Invoke(behaviour, null);
        }

        static void WaitForTexture(DentalCtVolumeService service)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while ((service.CurrentSliceTexture == null || service.StatusMessage.Contains("生成中")) &&
                   DateTime.UtcNow < deadline)
            {
                InvokeLifecycle(service, "Update");
                Thread.Sleep(2);
            }
            Assert.That(service.CurrentSliceTexture, Is.Not.Null, service.StatusMessage);
            StringAssert.DoesNotContain("生成中", service.StatusMessage);
        }

        sealed class BlockingSliceRenderer : IDicomSlicePixelRenderer
        {
            int m_CallCount;
            int m_ActiveCalls;
            int m_MaximumConcurrentCalls;

            public readonly ManualResetEventSlim FirstStarted = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseFirst = new ManualResetEventSlim(false);
            public int MaximumConcurrentCalls => Volatile.Read(ref m_MaximumConcurrentCalls);

            public byte[] Render(DicomVolume volume, DicomSliceRequest request, Func<bool> isStale)
            {
                var call = Interlocked.Increment(ref m_CallCount);
                var active = Interlocked.Increment(ref m_ActiveCalls);
                UpdateMaximum(active);
                try
                {
                    if (call == 1)
                    {
                        FirstStarted.Set();
                        if (!ReleaseFirst.Wait(TimeSpan.FromSeconds(2)))
                            throw new TimeoutException("Test renderer was not released.");
                    }
                    if (isStale())
                        throw new OperationCanceledException();
                    return Enumerable.Repeat((byte)call, request.PixelWidth * request.PixelHeight).ToArray();
                }
                finally
                {
                    Interlocked.Decrement(ref m_ActiveCalls);
                }
            }

            void UpdateMaximum(int active)
            {
                while (true)
                {
                    var observed = Volatile.Read(ref m_MaximumConcurrentCalls);
                    if (observed >= active ||
                        Interlocked.CompareExchange(ref m_MaximumConcurrentCalls, active, observed) == observed)
                        return;
                }
            }
        }

        sealed class BlockingDicomDecoder : IDicomDecoder
        {
            readonly ExplicitVrLittleEndianDicomDecoder m_Inner = new ExplicitVrLittleEndianDicomDecoder();

            public readonly ManualResetEventSlim Started = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);

            public bool TryDecode(string filePath, out DicomDecodedFile decoded, out string error)
            {
                decoded = null;
                error = string.Empty;
                Started.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(2)))
                {
                    error = "Test decoder was not released.";
                    return false;
                }
                return m_Inner.TryDecode(filePath, out decoded, out error);
            }
        }

        static DicomImageFrame Decode(byte[] bytes)
        {
            var decoder = new ExplicitVrLittleEndianDicomDecoder();
            using (var stream = new MemoryStream(bytes, false))
            {
                Assert.That(decoder.TryDecode(stream, "fixture.dcm", out var decoded, out var error), Is.True, error);
                return decoded.Frames[0];
            }
        }

        static byte[] Slice(byte[] source, int offset, int length)
        {
            var result = new byte[length];
            Array.Copy(source, offset, result, 0, length);
            return result;
        }

        static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        static class DicomFixture
        {
            public const string SopInstanceUid = "1.2.826.0.1.3680043.10.543.1";
            public const string SecondSopInstanceUid = "1.2.826.0.1.3680043.10.543.2";
            public const string FrameOfReferenceUid = "1.2.826.0.1.3680043.10.543.99";
            public const string SeriesInstanceUid = "1.2.826.0.1.3680043.10.543.20";
            public const string SecondSeriesInstanceUid = "1.2.826.0.1.3680043.10.543.21";
            static readonly HashSet<string> s_LongVrs = new HashSet<string> { "OB", "OD", "OF", "OL", "OV", "OW", "SQ", "UC", "UR", "UT", "UN" };

            public static byte[] Create(
                double positionZ,
                int[] rawPixels,
                double slope = 1,
                double intercept = 0,
                double windowCenter = 0,
                double windowWidth = 4096,
                string transferSyntax = ExplicitVrLittleEndianDicomDecoder.SupportedTransferSyntaxUid,
                double sliceSpacing = 2,
                string sopInstanceUid = SopInstanceUid,
                string seriesInstanceUid = SeriesInstanceUid)
            {
                if (rawPixels == null || rawPixels.Length == 0 || rawPixels.Length % 4 != 0)
                    throw new ArgumentException("Fixture frames must each contain 2x2 pixels.", nameof(rawPixels));
                var frameCount = rawPixels.Length / 4;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
                {
                    writer.Write(new byte[128]);
                    writer.Write(Encoding.ASCII.GetBytes("DICM"));
                    WriteText(writer, 0x0002, 0x0010, "UI", transferSyntax, '\0');
                    WriteText(writer, 0x0008, 0x0018, "UI", sopInstanceUid, '\0');
                    WriteText(writer, 0x0018, 0x0050, "DS", sliceSpacing.ToString(CultureInfo.InvariantCulture), ' ');
                    if (frameCount > 1)
                        WriteText(writer, 0x0018, 0x0088, "DS", sliceSpacing.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteText(writer, 0x0020, 0x000e, "UI", seriesInstanceUid, '\0');
                    WriteText(writer, 0x0020, 0x0032, "DS", "0\\0\\" + positionZ.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteText(writer, 0x0020, 0x0037, "DS", "1\\0\\0\\0\\1\\0", ' ');
                    WriteText(writer, 0x0020, 0x0052, "UI", FrameOfReferenceUid, '\0');
                    WriteUs(writer, 0x0028, 0x0002, 1);
                    WriteText(writer, 0x0028, 0x0004, "CS", "MONOCHROME2", ' ');
                    if (frameCount > 1)
                        WriteText(writer, 0x0028, 0x0008, "IS", frameCount.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteUs(writer, 0x0028, 0x0010, 2);
                    WriteUs(writer, 0x0028, 0x0011, 2);
                    WriteText(writer, 0x0028, 0x0030, "DS", "0.75\\0.5", ' ');
                    WriteUs(writer, 0x0028, 0x0100, 16);
                    WriteUs(writer, 0x0028, 0x0101, 12);
                    WriteUs(writer, 0x0028, 0x0102, 11);
                    WriteUs(writer, 0x0028, 0x0103, 1);
                    WriteText(writer, 0x0028, 0x1050, "DS", windowCenter.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteText(writer, 0x0028, 0x1051, "DS", windowWidth.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteText(writer, 0x0028, 0x1052, "DS", intercept.ToString(CultureInfo.InvariantCulture), ' ');
                    WriteText(writer, 0x0028, 0x1053, "DS", slope.ToString(CultureInfo.InvariantCulture), ' ');

                    var pixels = new byte[rawPixels.Length * 2];
                    for (var i = 0; i < rawPixels.Length; i++)
                    {
                        var stored = (ushort)(rawPixels[i] & 0x0fff);
                        pixels[i * 2] = (byte)stored;
                        pixels[i * 2 + 1] = (byte)(stored >> 8);
                    }
                    WriteElement(writer, 0x7fe0, 0x0010, "OW", pixels);
                    writer.Flush();
                    return stream.ToArray();
                }
            }

            static void WriteUs(BinaryWriter writer, ushort group, ushort element, ushort value)
            {
                WriteElement(writer, group, element, "US", new[] { (byte)value, (byte)(value >> 8) });
            }

            static void WriteText(BinaryWriter writer, ushort group, ushort element, string vr, string value, char padding)
            {
                var bytes = Encoding.ASCII.GetBytes(value);
                if ((bytes.Length & 1) != 0)
                    bytes = bytes.Concat(new[] { (byte)padding }).ToArray();
                WriteElement(writer, group, element, vr, bytes);
            }

            static void WriteElement(BinaryWriter writer, ushort group, ushort element, string vr, byte[] value)
            {
                writer.Write(group);
                writer.Write(element);
                writer.Write(Encoding.ASCII.GetBytes(vr));
                if (s_LongVrs.Contains(vr))
                {
                    writer.Write((ushort)0);
                    writer.Write((uint)value.Length);
                }
                else
                {
                    writer.Write((ushort)value.Length);
                }
                writer.Write(value);
            }
        }
    }
}
