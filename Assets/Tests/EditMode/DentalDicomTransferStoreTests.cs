using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalDicomTransferStoreTests
    {
        string m_Directory;

        [SetUp]
        public void SetUp()
        {
            m_Directory = Path.Combine(Path.GetTempPath(), "DentalDicomTransferStoreTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Directory))
                Directory.Delete(m_Directory, true);
        }

        [Test]
        public void CompletedAsset_RestoresFullCoverageAfterStoreRecreationAndCompletesIdempotently()
        {
            var bytes = Payload();
            var descriptor = Descriptor(bytes);
            var original = new DentalDicomTransferStore(m_Directory);
            var completedPath = ReceiveAndComplete(original, descriptor, bytes);
            Assert.That(original.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var retryPath, out var error), Is.True, error);
            Assert.That(retryPath, Is.EqualTo(completedPath));

            var restarted = new DentalDicomTransferStore(m_Directory);
            Assert.That(restarted.BeginAsset(descriptor, out error), Is.True, error);
            Assert.That(restarted.TryGetProgress(descriptor.TransferNamespace, descriptor.AssetId,
                out var progress), Is.True);
            Assert.That(progress.IsComplete, Is.True);
            Assert.That(progress.NextMissingOffset, Is.EqualTo(bytes.LongLength));
            Assert.That(progress.CoveredBytes, Is.EqualTo(bytes.LongLength));
            Assert.That(Directory.GetFiles(m_Directory, "*.part"), Is.Empty);
            Assert.That(Directory.GetFiles(m_Directory, "*.progress"), Is.Empty);
            Assert.That(restarted.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var resumedPath, out error), Is.True, error);
            Assert.That(resumedPath, Is.EqualTo(completedPath));
            Assert.That(restarted.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out retryPath, out error), Is.True, error);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(retryPath));

            // In-flight duplicate chunks must neither change final bytes nor create a new part.
            Assert.That(restarted.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId,
                0, bytes, out error), Is.True, error);
            var conflicting = (byte[])bytes.Clone();
            conflicting[0] ^= 0xff;
            Assert.That(restarted.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId,
                0, conflicting, out error), Is.False);
            StringAssert.Contains("conflicts", error);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(completedPath));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CorruptFinal_IsNotResumedAndCanBeRepairedWithoutDiscardingOldBytes(bool truncate)
        {
            var bytes = Payload();
            var descriptor = Descriptor(bytes);
            var completedPath = ReceiveAndComplete(new DentalDicomTransferStore(m_Directory), descriptor, bytes);
            var damaged = truncate ? bytes.Take(bytes.Length - 1).ToArray() : (byte[])bytes.Clone();
            if (!truncate)
                damaged[0] ^= 0xff;
            File.WriteAllBytes(completedPath, damaged);

            var restarted = new DentalDicomTransferStore(m_Directory);
            Assert.That(restarted.BeginAsset(descriptor, out var error), Is.True, error);
            Assert.That(restarted.TryGetProgress(descriptor.TransferNamespace, descriptor.AssetId,
                out var progress), Is.True);
            Assert.That(progress.IsComplete, Is.False);
            Assert.That(progress.NextMissingOffset, Is.Zero);
            Assert.That(restarted.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out _, out error), Is.False);
            CollectionAssert.AreEqual(damaged, File.ReadAllBytes(completedPath));

            var split = bytes.Length / 2;
            Assert.That(restarted.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId,
                0, bytes.Take(split).ToArray(), out error), Is.True, error);
            // A second restart during repair still resumes the partial bytes, not the corrupt final.
            var repair = new DentalDicomTransferStore(m_Directory);
            Assert.That(repair.BeginAsset(descriptor, out error), Is.True, error);
            Assert.That(repair.TryGetProgress(descriptor.TransferNamespace, descriptor.AssetId,
                out progress), Is.True);
            Assert.That(progress.NextMissingOffset, Is.EqualTo(split));
            Assert.That(repair.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId,
                split, bytes.Skip(split).ToArray(), out error), Is.True, error);
            Assert.That(repair.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var repairedPath, out error), Is.True, error);
            Assert.That(repairedPath, Is.EqualTo(completedPath));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(repairedPath));
            var backups = Directory.GetFiles(m_Directory, "*.dcm.corrupt-*");
            Assert.That(backups.Length, Is.EqualTo(1));
            CollectionAssert.AreEqual(damaged, File.ReadAllBytes(backups[0]));
        }

        [Test]
        public void VerifiedFinal_RecoversInterruptedRenameEvenWithAnOrphanedProgressJournal()
        {
            var bytes = Payload();
            var descriptor = Descriptor(bytes);
            var store = new DentalDicomTransferStore(m_Directory);
            Assert.That(store.BeginAsset(descriptor, out var error), Is.True, error);
            Assert.That(store.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId, 0, bytes, out error), Is.True, error);
            var progressPath = Directory.GetFiles(m_Directory, "*.progress").Single();
            var journal = File.ReadAllBytes(progressPath);
            Assert.That(store.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var completedPath, out error), Is.True, error);
            // Model a process exit after .part -> .dcm but before deleting the journal.
            File.WriteAllBytes(progressPath, journal);

            var restarted = new DentalDicomTransferStore(m_Directory);
            Assert.That(restarted.BeginAsset(descriptor, out error), Is.True, error);
            Assert.That(restarted.TryGetProgress(descriptor.TransferNamespace, descriptor.AssetId,
                out var progress), Is.True);
            Assert.That(progress.NextMissingOffset, Is.EqualTo(bytes.LongLength));
            Assert.That(restarted.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var resumedPath, out error), Is.True, error);
            Assert.That(resumedPath, Is.EqualTo(completedPath));
            Assert.That(Directory.GetFiles(m_Directory, "*.part"), Is.Empty);
        }

        [Test]
        public void FinalChangedAfterBegin_IsRejectedByCompletionVerification()
        {
            var bytes = Payload();
            var descriptor = Descriptor(bytes);
            var completedPath = ReceiveAndComplete(new DentalDicomTransferStore(m_Directory), descriptor, bytes);
            var restarted = new DentalDicomTransferStore(m_Directory);
            Assert.That(restarted.BeginAsset(descriptor, out var error), Is.True, error);
            var damaged = (byte[])bytes.Clone();
            damaged[0] ^= 0xff;
            File.WriteAllBytes(completedPath, damaged);
            Assert.That(restarted.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out _, out error), Is.False);
            StringAssert.Contains("changed after verification", error);
        }

        static byte[] Payload() => Enumerable.Range(0, 8192).Select(i => (byte)(i * 37)).ToArray();

        static DicomTransferFileDescriptor Descriptor(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return new DicomTransferFileDescriptor("same-session-and-transfer", "ct-slice-1", "slice001.dcm",
                    bytes.LongLength, BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
        }

        static string ReceiveAndComplete(DentalDicomTransferStore store, DicomTransferFileDescriptor descriptor, byte[] bytes)
        {
            Assert.That(store.BeginAsset(descriptor, out var error), Is.True, error);
            Assert.That(store.WriteChunk(descriptor.TransferNamespace, descriptor.AssetId, 0, bytes, out error), Is.True, error);
            Assert.That(store.TryCompleteAsset(descriptor.TransferNamespace, descriptor.AssetId,
                out var completedPath, out error), Is.True, error);
            return completedPath;
        }
    }
}
