using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Unity.XR.XREAL.Samples
{
    public sealed class DicomTransferFileDescriptor
    {
        public const string DefaultTransferNamespace = "local-v1";

        public string TransferNamespace { get; }
        public string AssetId { get; }
        public string FileName { get; }
        public long ExpectedBytes { get; }
        public string Sha256Hex { get; }

        public DicomTransferFileDescriptor(string assetId, string fileName, long expectedBytes, string sha256Hex)
            : this(DefaultTransferNamespace, assetId, fileName, expectedBytes, sha256Hex)
        {
        }

        public DicomTransferFileDescriptor(
            string transferNamespace,
            string assetId,
            string fileName,
            long expectedBytes,
            string sha256Hex)
        {
            TransferNamespace = transferNamespace ?? string.Empty;
            AssetId = assetId ?? string.Empty;
            FileName = fileName ?? string.Empty;
            ExpectedBytes = expectedBytes;
            Sha256Hex = NormalizeSha256(sha256Hex);
        }

        internal static string NormalizeSha256(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }

    public readonly struct DicomByteRange
    {
        public readonly long Start;
        public readonly long EndExclusive;

        public DicomByteRange(long start, long endExclusive)
        {
            Start = start;
            EndExclusive = endExclusive;
        }

        public long Length => EndExclusive - Start;
    }

    public sealed class DicomTransferProgress
    {
        public string TransferNamespace { get; }
        public string AssetId { get; }
        public string FileName { get; }
        public long ExpectedBytes { get; }
        public string Sha256Hex { get; }
        public long CoveredBytes { get; }
        public long NextMissingOffset { get; }
        public IReadOnlyList<DicomByteRange> CoveredRanges { get; }
        public bool IsComplete { get; }

        internal DicomTransferProgress(DicomTransferFileDescriptor descriptor, IReadOnlyList<DicomByteRange> ranges)
        {
            TransferNamespace = descriptor.TransferNamespace;
            AssetId = descriptor.AssetId;
            FileName = descriptor.FileName;
            ExpectedBytes = descriptor.ExpectedBytes;
            Sha256Hex = descriptor.Sha256Hex;
            CoveredRanges = ranges;
            CoveredBytes = ranges.Sum(range => range.Length);
            var nextMissing = 0L;
            foreach (var range in ranges)
            {
                if (range.Start > nextMissing)
                    break;
                nextMissing = Math.Max(nextMissing, range.EndExclusive);
            }
            NextMissingOffset = nextMissing;
            IsComplete = ranges.Count == 1 && ranges[0].Start == 0 && ranges[0].EndExclusive == descriptor.ExpectedBytes;
        }
    }

    /// <summary>
    /// Stores DICOM assets as bounded random-access .part files. Coverage is persisted after every
    /// flushed chunk, so reconnecting clients can resume without treating sparse zeroes as data.
    /// </summary>
    public sealed class DentalDicomTransferStore
    {
        public const long DefaultMaximumFileBytes = 2L * 1024 * 1024 * 1024;

        readonly string m_RootDirectory;
        readonly long m_MaximumFileBytes;
        readonly Dictionary<TransferAssetKey, DicomChunkFileReceiver> m_Receivers =
            new Dictionary<TransferAssetKey, DicomChunkFileReceiver>();

        public DentalDicomTransferStore(string rootDirectory, long maximumFileBytes = DefaultMaximumFileBytes)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A transfer storage directory is required.", nameof(rootDirectory));
            if (maximumFileBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));

            m_RootDirectory = Path.GetFullPath(rootDirectory);
            m_MaximumFileBytes = maximumFileBytes;
            Directory.CreateDirectory(m_RootDirectory);
        }

        public bool BeginAsset(DicomTransferFileDescriptor descriptor, out string error)
        {
            error = string.Empty;
            if (!ValidateDescriptor(descriptor, m_MaximumFileBytes, out error))
                return false;

            lock (m_Receivers)
            {
                var key = new TransferAssetKey(descriptor.TransferNamespace, descriptor.AssetId);
                if (m_Receivers.TryGetValue(key, out var existing))
                {
                    if (existing.Matches(descriptor))
                        return true;

                    error = "An asset with the same id is already open with different metadata.";
                    return false;
                }

                try
                {
                    var receiver = new DicomChunkFileReceiver(m_RootDirectory, descriptor);
                    m_Receivers.Add(key, receiver);
                    return true;
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        public bool WriteChunk(string assetId, long offset, byte[] data, out string error)
        {
            return WriteChunk(DicomTransferFileDescriptor.DefaultTransferNamespace, assetId, offset, data, out error);
        }

        public bool WriteChunk(string transferNamespace, string assetId, long offset, byte[] data, out string error)
        {
            error = string.Empty;
            if (!TryGetReceiver(transferNamespace, assetId, out var receiver, out error))
                return false;
            return receiver.WriteChunk(offset, data, out error);
        }

        public bool TryCompleteAsset(string assetId, out string completedPath, out string error)
        {
            return TryCompleteAsset(
                DicomTransferFileDescriptor.DefaultTransferNamespace,
                assetId,
                out completedPath,
                out error);
        }

        public bool TryCompleteAsset(
            string transferNamespace,
            string assetId,
            out string completedPath,
            out string error)
        {
            completedPath = string.Empty;
            error = string.Empty;
            if (!TryGetReceiver(transferNamespace, assetId, out var receiver, out error))
                return false;

            if (!receiver.TryComplete(out completedPath, out error))
                return false;

            lock (m_Receivers)
                m_Receivers.Remove(new TransferAssetKey(transferNamespace, assetId));
            return true;
        }

        public bool TryGetProgress(string assetId, out DicomTransferProgress progress)
        {
            return TryGetProgress(DicomTransferFileDescriptor.DefaultTransferNamespace, assetId, out progress);
        }

        public bool TryGetProgress(
            string transferNamespace,
            string assetId,
            out DicomTransferProgress progress)
        {
            progress = null;
            lock (m_Receivers)
            {
                var key = new TransferAssetKey(transferNamespace, assetId);
                if (!key.IsValid || !m_Receivers.TryGetValue(key, out var receiver))
                    return false;
                progress = receiver.Progress;
                return true;
            }
        }

        bool TryGetReceiver(
            string transferNamespace,
            string assetId,
            out DicomChunkFileReceiver receiver,
            out string error)
        {
            receiver = null;
            lock (m_Receivers)
            {
                var key = new TransferAssetKey(transferNamespace, assetId);
                if (!key.IsValid || !m_Receivers.TryGetValue(key, out receiver))
                {
                    error = "The DICOM asset has not been started in this transfer namespace.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        static bool ValidateDescriptor(DicomTransferFileDescriptor descriptor, long maximumFileBytes, out string error)
        {
            if (descriptor == null)
            {
                error = "DICOM asset descriptor is missing.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(descriptor.TransferNamespace) || descriptor.TransferNamespace.Length > 256)
            {
                error = "DICOM transfer namespace is empty or too long.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(descriptor.AssetId) || descriptor.AssetId.Length > 256)
            {
                error = "DICOM asset id is empty or too long.";
                return false;
            }
            if (descriptor.ExpectedBytes <= 0 || descriptor.ExpectedBytes > maximumFileBytes)
            {
                error = $"DICOM asset size must be between 1 and {maximumFileBytes} bytes.";
                return false;
            }
            if (descriptor.Sha256Hex.Length != 64 || descriptor.Sha256Hex.Any(c => !Uri.IsHexDigit(c)))
            {
                error = "DICOM asset SHA-256 must contain exactly 64 hexadecimal characters.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        readonly struct TransferAssetKey : IEquatable<TransferAssetKey>
        {
            readonly string m_TransferNamespace;
            readonly string m_AssetId;

            public TransferAssetKey(string transferNamespace, string assetId)
            {
                m_TransferNamespace = transferNamespace ?? string.Empty;
                m_AssetId = assetId ?? string.Empty;
            }

            public bool IsValid => !string.IsNullOrWhiteSpace(m_TransferNamespace) &&
                                   !string.IsNullOrWhiteSpace(m_AssetId);

            public bool Equals(TransferAssetKey other)
            {
                return string.Equals(m_TransferNamespace, other.m_TransferNamespace, StringComparison.Ordinal) &&
                       string.Equals(m_AssetId, other.m_AssetId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is TransferAssetKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((m_TransferNamespace != null ? StringComparer.Ordinal.GetHashCode(m_TransferNamespace) : 0) * 397) ^
                           (m_AssetId != null ? StringComparer.Ordinal.GetHashCode(m_AssetId) : 0);
                }
            }
        }

        sealed class DicomChunkFileReceiver
        {
            const string ProgressHeader = "DICOM-CHUNK-V2";

            readonly object m_Gate = new object();
            readonly DicomTransferFileDescriptor m_Descriptor;
            readonly string m_PartPath;
            readonly string m_ProgressPath;
            readonly string m_FinalPath;
            readonly List<DicomByteRange> m_Ranges = new List<DicomByteRange>();

            public DicomChunkFileReceiver(string rootDirectory, DicomTransferFileDescriptor descriptor)
            {
                m_Descriptor = descriptor;
                var storageName = ComputeHex(Encoding.UTF8.GetBytes(
                    descriptor.TransferNamespace + "\n" + descriptor.AssetId));
                m_PartPath = Path.Combine(rootDirectory, storageName + ".part");
                m_ProgressPath = Path.Combine(rootDirectory, storageName + ".progress");
                m_FinalPath = Path.Combine(rootDirectory, storageName + ".dcm");

                if (File.Exists(m_ProgressPath))
                    LoadProgress();
                else
                    InitializeNewPart();
            }

            public DicomTransferProgress Progress
            {
                get
                {
                    lock (m_Gate)
                        return new DicomTransferProgress(m_Descriptor, m_Ranges.ToArray());
                }
            }

            public bool Matches(DicomTransferFileDescriptor other)
            {
                return other != null &&
                       string.Equals(m_Descriptor.TransferNamespace, other.TransferNamespace, StringComparison.Ordinal) &&
                       string.Equals(m_Descriptor.AssetId, other.AssetId, StringComparison.Ordinal) &&
                       string.Equals(m_Descriptor.FileName, other.FileName, StringComparison.Ordinal) &&
                       m_Descriptor.ExpectedBytes == other.ExpectedBytes &&
                       FixedTimeEquals(m_Descriptor.Sha256Hex, other.Sha256Hex);
            }

            public bool WriteChunk(long offset, byte[] data, out string error)
            {
                lock (m_Gate)
                {
                    error = string.Empty;
                    if (data == null || data.Length == 0)
                    {
                        error = "DICOM chunk is empty.";
                        return false;
                    }
                    if (offset < 0 || offset > m_Descriptor.ExpectedBytes ||
                        data.LongLength > m_Descriptor.ExpectedBytes - offset)
                    {
                        error = "DICOM chunk lies outside the declared asset size.";
                        return false;
                    }
                    if (!File.Exists(m_PartPath))
                    {
                        error = "DICOM partial file is missing; restart this asset transfer.";
                        return false;
                    }

                    try
                    {
                        using (var stream = new FileStream(m_PartPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                        {
                            if (stream.Length != m_Descriptor.ExpectedBytes)
                            {
                                error = "DICOM partial file size does not match its resume state.";
                                return false;
                            }

                            if (!VerifyOverlappingBytes(stream, offset, data, out error))
                                return false;

                            stream.Position = offset;
                            stream.Write(data, 0, data.Length);
                            FlushDurably(stream);
                        }

                        AddCoveredRange(offset, offset + data.LongLength);
                        PersistProgress();
                        return true;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        error = exception.Message;
                        return false;
                    }
                }
            }

            public bool TryComplete(out string completedPath, out string error)
            {
                lock (m_Gate)
                {
                    completedPath = string.Empty;
                    error = string.Empty;
                    var progress = new DicomTransferProgress(m_Descriptor, m_Ranges.ToArray());
                    if (!progress.IsComplete)
                    {
                        error = $"DICOM asset is incomplete ({progress.CoveredBytes}/{progress.ExpectedBytes} bytes covered).";
                        return false;
                    }
                    if (!File.Exists(m_PartPath) || new FileInfo(m_PartPath).Length != m_Descriptor.ExpectedBytes)
                    {
                        error = "DICOM partial file has an unexpected size.";
                        return false;
                    }

                    try
                    {
                        string actualHash;
                        using (var stream = new FileStream(m_PartPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var sha = SHA256.Create())
                            actualHash = ToHex(sha.ComputeHash(stream));

                        if (!FixedTimeEquals(actualHash, m_Descriptor.Sha256Hex))
                        {
                            error = $"DICOM SHA-256 mismatch. expected={m_Descriptor.Sha256Hex}, actual={actualHash}";
                            return false;
                        }

                        if (File.Exists(m_FinalPath))
                        {
                            string existingHash;
                            using (var stream = new FileStream(m_FinalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var sha = SHA256.Create())
                                existingHash = ToHex(sha.ComputeHash(stream));
                            if (!FixedTimeEquals(existingHash, actualHash))
                            {
                                error = "A completed DICOM file with the same asset id contains different data.";
                                return false;
                            }
                            File.Delete(m_PartPath);
                        }
                        else
                        {
                            File.Move(m_PartPath, m_FinalPath);
                        }

                        if (File.Exists(m_ProgressPath))
                            File.Delete(m_ProgressPath);
                        completedPath = m_FinalPath;
                        return true;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is CryptographicException)
                    {
                        error = exception.Message;
                        return false;
                    }
                }
            }

            void InitializeNewPart()
            {
                if (File.Exists(m_PartPath))
                    File.Delete(m_PartPath);
                using (var stream = new FileStream(m_PartPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                {
                    stream.SetLength(m_Descriptor.ExpectedBytes);
                    FlushDurably(stream);
                }
                PersistProgress();
            }

            void LoadProgress()
            {
                if (!File.Exists(m_PartPath))
                    throw new InvalidDataException("DICOM resume state exists but its partial file is missing.");

                var lines = File.ReadAllLines(m_ProgressPath);
                if (lines.Length < 7 || lines[0] != ProgressHeader)
                    throw new InvalidDataException("DICOM resume state is malformed.");

                var transferNamespace = DecodeLine(lines[1], "namespace=");
                var assetId = DecodeLine(lines[2], "asset=");
                var fileName = DecodeLine(lines[3], "name=");
                if (!long.TryParse(ValueOf(lines[4], "size="), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                    throw new InvalidDataException("DICOM resume size is malformed.");
                var hash = DicomTransferFileDescriptor.NormalizeSha256(ValueOf(lines[5], "sha256="));

                var restored = new DicomTransferFileDescriptor(transferNamespace, assetId, fileName, size, hash);
                if (!Matches(restored))
                    throw new InvalidDataException("DICOM resume state does not match the requested asset.");
                if (new FileInfo(m_PartPath).Length != m_Descriptor.ExpectedBytes)
                    throw new InvalidDataException("DICOM partial file size does not match its resume state.");

                var encodedRanges = ValueOf(lines[6], "ranges=");
                if (!string.IsNullOrWhiteSpace(encodedRanges))
                {
                    foreach (var encodedRange in encodedRanges.Split(','))
                    {
                        var parts = encodedRange.Split('-');
                        if (parts.Length != 2 ||
                            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                            start < 0 || end <= start || end > m_Descriptor.ExpectedBytes)
                            throw new InvalidDataException("DICOM resume coverage contains an invalid range.");
                        m_Ranges.Add(new DicomByteRange(start, end));
                    }
                    NormalizeRanges();
                }
            }

            bool VerifyOverlappingBytes(FileStream stream, long chunkStart, byte[] data, out string error)
            {
                var chunkEnd = chunkStart + data.LongLength;
                var scratch = new byte[Math.Min(data.Length, 64 * 1024)];
                foreach (var range in m_Ranges)
                {
                    var overlapStart = Math.Max(chunkStart, range.Start);
                    var overlapEnd = Math.Min(chunkEnd, range.EndExclusive);
                    if (overlapEnd <= overlapStart)
                        continue;

                    var remaining = overlapEnd - overlapStart;
                    var dataOffset = overlapStart - chunkStart;
                    stream.Position = overlapStart;
                    while (remaining > 0)
                    {
                        var count = (int)Math.Min(remaining, scratch.Length);
                        if (!ReadExactly(stream, scratch, count))
                        {
                            error = "Could not read previously covered DICOM bytes.";
                            return false;
                        }
                        for (var i = 0; i < count; i++)
                        {
                            if (scratch[i] != data[dataOffset + i])
                            {
                                error = "A retried DICOM chunk conflicts with bytes already stored.";
                                return false;
                            }
                        }
                        remaining -= count;
                        dataOffset += count;
                    }
                }

                error = string.Empty;
                return true;
            }

            void AddCoveredRange(long start, long endExclusive)
            {
                m_Ranges.Add(new DicomByteRange(start, endExclusive));
                NormalizeRanges();
            }

            void NormalizeRanges()
            {
                if (m_Ranges.Count < 2)
                    return;
                m_Ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
                var merged = new List<DicomByteRange>(m_Ranges.Count);
                var current = m_Ranges[0];
                for (var i = 1; i < m_Ranges.Count; i++)
                {
                    var next = m_Ranges[i];
                    if (next.Start <= current.EndExclusive)
                        current = new DicomByteRange(current.Start, Math.Max(current.EndExclusive, next.EndExclusive));
                    else
                    {
                        merged.Add(current);
                        current = next;
                    }
                }
                merged.Add(current);
                m_Ranges.Clear();
                m_Ranges.AddRange(merged);
            }

            void PersistProgress()
            {
                var builder = new StringBuilder();
                builder.AppendLine(ProgressHeader);
                builder.Append("namespace=").AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(m_Descriptor.TransferNamespace)));
                builder.Append("asset=").AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(m_Descriptor.AssetId)));
                builder.Append("name=").AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(m_Descriptor.FileName)));
                builder.Append("size=").AppendLine(m_Descriptor.ExpectedBytes.ToString(CultureInfo.InvariantCulture));
                builder.Append("sha256=").AppendLine(m_Descriptor.Sha256Hex);
                builder.Append("ranges=").AppendLine(string.Join(",", m_Ranges.Select(range =>
                    range.Start.ToString(CultureInfo.InvariantCulture) + "-" + range.EndExclusive.ToString(CultureInfo.InvariantCulture))));

                var temporaryPath = m_ProgressPath + ".tmp";
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(builder.ToString());
                    writer.Flush();
                    FlushDurably(stream);
                }

                if (File.Exists(m_ProgressPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, m_ProgressPath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(m_ProgressPath);
                        File.Move(temporaryPath, m_ProgressPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, m_ProgressPath);
                }
            }

            static string DecodeLine(string line, string prefix)
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(ValueOf(line, prefix)));
                }
                catch (FormatException exception)
                {
                    throw new InvalidDataException("DICOM resume text is malformed.", exception);
                }
            }

            static string ValueOf(string line, string prefix)
            {
                if (line == null || !line.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidDataException("DICOM resume state is malformed.");
                return line.Substring(prefix.Length);
            }

            static void FlushDurably(FileStream stream)
            {
                try
                {
                    stream.Flush(true);
                }
                catch (PlatformNotSupportedException)
                {
                    stream.Flush();
                }
            }

            static bool ReadExactly(Stream stream, byte[] buffer, int count)
            {
                var offset = 0;
                while (offset < count)
                {
                    var read = stream.Read(buffer, offset, count - offset);
                    if (read <= 0)
                        return false;
                    offset += read;
                }
                return true;
            }
        }

        internal static string ComputeHex(byte[] data)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(data));
        }

        internal static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var i = 0; i < left.Length; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
