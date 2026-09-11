using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Unity.XR.XREAL.Samples
{
    public interface IDicomDecoder
    {
        bool TryDecode(string filePath, out DicomDecodedFile decoded, out string error);
    }

    public sealed class DicomDecodedFile
    {
        public string SourceName { get; }
        public string SopInstanceUid { get; }
        public string FrameOfReferenceUid { get; }
        public string SeriesInstanceUid { get; }
        public IReadOnlyList<DicomImageFrame> Frames { get; }

        public DicomDecodedFile(string sourceName, string sopInstanceUid, IReadOnlyList<DicomImageFrame> frames)
        {
            SourceName = sourceName ?? string.Empty;
            SopInstanceUid = sopInstanceUid ?? string.Empty;
            Frames = frames ?? throw new ArgumentNullException(nameof(frames));
            FrameOfReferenceUid = frames.Count > 0 ? frames[0].FrameOfReferenceUid : string.Empty;
            SeriesInstanceUid = frames.Count > 0 ? frames[0].SeriesInstanceUid : string.Empty;
        }
    }

    public sealed class DicomImageFrame
    {
        public string SopInstanceUid { get; }
        public string FrameOfReferenceUid { get; }
        public string SeriesInstanceUid { get; }
        public int FrameNumber { get; }
        public int Rows { get; }
        public int Columns { get; }
        public int[] RawPixels { get; }
        public double RescaleSlope { get; }
        public double RescaleIntercept { get; }
        public double? WindowCenter { get; }
        public double? WindowWidth { get; }
        public bool IsMonochrome1 { get; }
        public DicomImageGeometry Geometry { get; }

        public DicomImageFrame(
            string sopInstanceUid,
            int frameNumber,
            int rows,
            int columns,
            int[] rawPixels,
            double rescaleSlope,
            double rescaleIntercept,
            double? windowCenter,
            double? windowWidth,
            bool isMonochrome1,
            DicomImageGeometry geometry,
            string frameOfReferenceUid = "",
            string seriesInstanceUid = "")
        {
            SopInstanceUid = sopInstanceUid ?? string.Empty;
            FrameNumber = frameNumber;
            Rows = rows;
            Columns = columns;
            RawPixels = rawPixels ?? throw new ArgumentNullException(nameof(rawPixels));
            RescaleSlope = rescaleSlope;
            RescaleIntercept = rescaleIntercept;
            WindowCenter = windowCenter;
            WindowWidth = windowWidth;
            IsMonochrome1 = isMonochrome1;
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            FrameOfReferenceUid = frameOfReferenceUid ?? string.Empty;
            SeriesInstanceUid = seriesInstanceUid ?? string.Empty;
        }

        public double GetRescaledPixel(int row, int column)
        {
            if (row < 0 || row >= Rows)
                throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= Columns)
                throw new ArgumentOutOfRangeException(nameof(column));
            return RawPixels[row * Columns + column] * RescaleSlope + RescaleIntercept;
        }
    }

    public sealed class DicomImageGeometry
    {
        public DicomVector3d ImagePositionPatient { get; }
        public DicomVector3d ColumnIndexDirection { get; }
        public DicomVector3d RowIndexDirection { get; }
        public DicomVector3d SliceDirection { get; }
        public double RowSpacingMm { get; }
        public double ColumnSpacingMm { get; }
        public double SuggestedSliceSpacingMm { get; }

        public DicomImageGeometry(
            DicomVector3d imagePositionPatient,
            DicomVector3d columnIndexDirection,
            DicomVector3d rowIndexDirection,
            double rowSpacingMm,
            double columnSpacingMm,
            double suggestedSliceSpacingMm)
        {
            if (rowSpacingMm <= 0 || columnSpacingMm <= 0 ||
                double.IsNaN(rowSpacingMm) || double.IsInfinity(rowSpacingMm) ||
                double.IsNaN(columnSpacingMm) || double.IsInfinity(columnSpacingMm) ||
                double.IsNaN(suggestedSliceSpacingMm) || double.IsInfinity(suggestedSliceSpacingMm))
                throw new ArgumentOutOfRangeException(nameof(rowSpacingMm), "DICOM pixel spacing must be positive.");

            ImagePositionPatient = imagePositionPatient;
            ColumnIndexDirection = columnIndexDirection.Normalized;
            RowIndexDirection = rowIndexDirection.Normalized;
            var dot = Math.Abs(DicomVector3d.Dot(ColumnIndexDirection, RowIndexDirection));
            if (dot > 1e-4)
                throw new ArgumentException("DICOM row and column directions must be orthogonal.");

            SliceDirection = DicomVector3d.Cross(ColumnIndexDirection, RowIndexDirection).Normalized;
            RowSpacingMm = rowSpacingMm;
            ColumnSpacingMm = columnSpacingMm;
            SuggestedSliceSpacingMm = suggestedSliceSpacingMm > 0 ? suggestedSliceSpacingMm : 0;
        }

        public DicomVector3d PixelCenterPatient(int row, int column)
        {
            return ImagePositionPatient +
                   ColumnIndexDirection * (column * ColumnSpacingMm) +
                   RowIndexDirection * (row * RowSpacingMm);
        }
    }

    public readonly struct DicomVector3d : IEquatable<DicomVector3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public DicomVector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        public DicomVector3d Normalized
        {
            get
            {
                var magnitude = Magnitude;
                if (magnitude < 1e-12)
                    throw new InvalidOperationException("Cannot normalize a zero DICOM direction vector.");
                return this * (1.0 / magnitude);
            }
        }

        public static DicomVector3d operator +(DicomVector3d left, DicomVector3d right) =>
            new DicomVector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static DicomVector3d operator -(DicomVector3d left, DicomVector3d right) =>
            new DicomVector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static DicomVector3d operator *(DicomVector3d value, double scale) =>
            new DicomVector3d(value.X * scale, value.Y * scale, value.Z * scale);

        public static double Dot(DicomVector3d left, DicomVector3d right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        public static DicomVector3d Cross(DicomVector3d left, DicomVector3d right) =>
            new DicomVector3d(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        public bool Equals(DicomVector3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is DicomVector3d other && Equals(other);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }

    /// <summary>
    /// Managed v1 decoder for uncompressed Explicit VR Little Endian DICOM Part 10 files.
    /// It deliberately rejects compressed/implicit/big-endian transfer syntaxes. A future native
    /// DCMTK implementation can replace it through IDicomDecoder without changing volume clients.
    /// </summary>
    public sealed class ExplicitVrLittleEndianDicomDecoder : IDicomDecoder
    {
        public const string SupportedTransferSyntaxUid = "1.2.840.10008.1.2.1";
        const uint UndefinedLength = 0xffffffff;
        const int MaximumMetadataValueBytes = 1024 * 1024;

        static readonly HashSet<string> s_LongValueRepresentations = new HashSet<string>(StringComparer.Ordinal)
        {
            "OB", "OD", "OF", "OL", "OV", "OW", "SQ", "SV", "UC", "UR", "UT", "UV", "UN",
        };

        public bool TryDecode(string filePath, out DicomDecodedFile decoded, out string error)
        {
            decoded = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                error = "DICOM file does not exist.";
                return false;
            }

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return TryDecode(stream, Path.GetFileName(filePath), out decoded, out error);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryDecode(Stream stream, string sourceName, out DicomDecodedFile decoded, out string error)
        {
            decoded = null;
            error = string.Empty;
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                error = "DICOM input must be a readable, seekable stream.";
                return false;
            }

            try
            {
                if (!ReadPart10Header(stream, out var transferSyntaxUid, out error))
                    return false;
                if (!string.Equals(transferSyntaxUid, SupportedTransferSyntaxUid, StringComparison.Ordinal))
                {
                    error = $"Unsupported DICOM transfer syntax '{transferSyntaxUid}'. Expected {SupportedTransferSyntaxUid}.";
                    return false;
                }

                return ReadDataset(stream, sourceName, out decoded, out error);
            }
            catch (Exception exception) when (
                exception is EndOfStreamException ||
                exception is InvalidDataException ||
                exception is OverflowException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                error = exception.Message;
                return false;
            }
        }

        static bool ReadPart10Header(Stream stream, out string transferSyntaxUid, out string error)
        {
            transferSyntaxUid = string.Empty;
            error = string.Empty;
            if (stream.Length - stream.Position < 132)
            {
                error = "DICOM Part 10 header is truncated.";
                return false;
            }

            stream.Position += 128;
            var marker = ReadBytes(stream, 4);
            if (marker[0] != (byte)'D' || marker[1] != (byte)'I' || marker[2] != (byte)'C' || marker[3] != (byte)'M')
            {
                error = "DICOM Part 10 marker is missing.";
                return false;
            }

            while (stream.Position < stream.Length)
            {
                var elementStart = stream.Position;
                var header = ReadElementHeader(stream);
                if (header.Group != 0x0002)
                {
                    stream.Position = elementStart;
                    break;
                }
                if (header.Length == UndefinedLength || header.Length > MaximumMetadataValueBytes)
                {
                    error = "DICOM file meta information contains an invalid value length.";
                    return false;
                }

                if (header.Element == 0x0010)
                    transferSyntaxUid = ReadDicomText(stream, checked((int)header.Length));
                else
                    SkipBytes(stream, header.Length);
            }

            if (string.IsNullOrEmpty(transferSyntaxUid))
            {
                error = "DICOM TransferSyntaxUID is missing.";
                return false;
            }
            return true;
        }

        static bool ReadDataset(Stream stream, string sourceName, out DicomDecodedFile decoded, out string error)
        {
            decoded = null;
            error = string.Empty;
            string sopInstanceUid = string.Empty;
            string frameOfReferenceUid = string.Empty;
            string seriesInstanceUid = string.Empty;
            string photometric = string.Empty;
            var rows = 0;
            var columns = 0;
            var samplesPerPixel = 1;
            var numberOfFrames = 1;
            var bitsAllocated = 0;
            var bitsStored = 0;
            var highBit = -1;
            var pixelRepresentation = 0;
            var rescaleSlope = 1.0;
            var rescaleIntercept = 0.0;
            double? windowCenter = null;
            double? windowWidth = null;
            var imagePosition = Array.Empty<double>();
            var imageOrientation = Array.Empty<double>();
            var pixelSpacing = Array.Empty<double>();
            var sliceThickness = 0.0;
            var spacingBetweenSlices = 0.0;
            long pixelDataOffset = -1;
            uint pixelDataLength = 0;

            while (stream.Position < stream.Length)
            {
                var header = ReadElementHeader(stream);
                if (header.Length == UndefinedLength)
                {
                    if (header.Vr != "SQ")
                    {
                        error = $"Undefined length is unsupported for DICOM tag ({header.Group:x4},{header.Element:x4}) VR {header.Vr}.";
                        return false;
                    }
                    SkipUndefinedSequence(stream);
                    continue;
                }

                var tag = ((uint)header.Group << 16) | header.Element;
                switch (tag)
                {
                    case 0x00080018:
                        sopInstanceUid = ReadLimitedText(stream, header);
                        break;
                    case 0x00180050:
                        sliceThickness = FirstNumber(ReadLimitedText(stream, header), 0);
                        break;
                    case 0x00180088:
                        spacingBetweenSlices = Math.Abs(FirstNumber(ReadLimitedText(stream, header), 0));
                        break;
                    case 0x00200032:
                        imagePosition = ParseNumbers(ReadLimitedText(stream, header));
                        break;
                    case 0x0020000e:
                        seriesInstanceUid = ReadLimitedText(stream, header);
                        break;
                    case 0x00200037:
                        imageOrientation = ParseNumbers(ReadLimitedText(stream, header));
                        break;
                    case 0x00200052:
                        frameOfReferenceUid = ReadLimitedText(stream, header);
                        break;
                    case 0x00280002:
                        samplesPerPixel = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280004:
                        photometric = ReadLimitedText(stream, header).ToUpperInvariant();
                        break;
                    case 0x00280008:
                        numberOfFrames = ParsePositiveInteger(ReadLimitedText(stream, header), "NumberOfFrames");
                        break;
                    case 0x00280010:
                        rows = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280011:
                        columns = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280030:
                        pixelSpacing = ParseNumbers(ReadLimitedText(stream, header));
                        break;
                    case 0x00280100:
                        bitsAllocated = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280101:
                        bitsStored = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280102:
                        highBit = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00280103:
                        pixelRepresentation = ReadUnsignedShortValue(stream, header);
                        break;
                    case 0x00281050:
                        windowCenter = FirstNumber(ReadLimitedText(stream, header), double.NaN);
                        if (double.IsNaN(windowCenter.Value)) windowCenter = null;
                        break;
                    case 0x00281051:
                        windowWidth = FirstNumber(ReadLimitedText(stream, header), double.NaN);
                        if (double.IsNaN(windowWidth.Value)) windowWidth = null;
                        break;
                    case 0x00281052:
                        rescaleIntercept = FirstNumber(ReadLimitedText(stream, header), 0);
                        break;
                    case 0x00281053:
                        rescaleSlope = FirstNumber(ReadLimitedText(stream, header), 1);
                        break;
                    case 0x7fe00010:
                        pixelDataOffset = stream.Position;
                        pixelDataLength = header.Length;
                        stream.Position = stream.Length;
                        break;
                    default:
                        SkipBytes(stream, header.Length);
                        break;
                }
            }

            if (rows <= 0 || columns <= 0 || numberOfFrames <= 0)
            {
                error = "DICOM rows, columns, or frame count is missing or invalid.";
                return false;
            }
            if (samplesPerPixel != 1 || (photometric != "MONOCHROME1" && photometric != "MONOCHROME2"))
            {
                error = "Only single-sample MONOCHROME1/2 DICOM images are supported.";
                return false;
            }
            if (bitsAllocated != 8 && bitsAllocated != 16)
            {
                error = "Only 8-bit and 16-bit uncompressed DICOM pixels are supported.";
                return false;
            }
            if (bitsStored == 0)
                bitsStored = bitsAllocated;
            if (highBit < 0)
                highBit = bitsStored - 1;
            if (bitsStored <= 0 || bitsStored > bitsAllocated || highBit < bitsStored - 1 || highBit >= bitsAllocated ||
                (pixelRepresentation != 0 && pixelRepresentation != 1))
            {
                error = "DICOM pixel bit layout is invalid.";
                return false;
            }
            if (imagePosition.Length != 3 || imageOrientation.Length != 6 || pixelSpacing.Length != 2 ||
                pixelSpacing[0] <= 0 || pixelSpacing[1] <= 0)
            {
                error = "DICOM ImagePositionPatient, ImageOrientationPatient, or PixelSpacing is missing or invalid.";
                return false;
            }
            if (pixelDataOffset < 0 || pixelDataLength == UndefinedLength)
            {
                error = "Uncompressed DICOM PixelData is missing.";
                return false;
            }

            var pixelCountPerFrame = checked(rows * columns);
            var totalPixelCount = checked(pixelCountPerFrame * numberOfFrames);
            var bytesPerPixel = bitsAllocated / 8;
            var requiredBytes = checked(totalPixelCount * bytesPerPixel);
            if (pixelDataLength < requiredBytes || pixelDataLength > requiredBytes + 1L)
            {
                error = $"DICOM PixelData length {pixelDataLength} does not match {requiredBytes} decoded bytes.";
                return false;
            }
            if (pixelDataOffset + pixelDataLength > stream.Length)
            {
                error = "DICOM PixelData is truncated.";
                return false;
            }

            stream.Position = pixelDataOffset;
            var pixelBytes = ReadBytes(stream, requiredBytes);
            var allPixels = DecodePixels(pixelBytes, bitsAllocated, bitsStored, highBit, pixelRepresentation);
            var position = new DicomVector3d(imagePosition[0], imagePosition[1], imagePosition[2]);
            var columnIndexDirection = new DicomVector3d(imageOrientation[0], imageOrientation[1], imageOrientation[2]);
            var rowIndexDirection = new DicomVector3d(imageOrientation[3], imageOrientation[4], imageOrientation[5]);
            var suggestedSpacing = spacingBetweenSlices > 0 ? spacingBetweenSlices : Math.Abs(sliceThickness);
            if (numberOfFrames > 1 && suggestedSpacing <= 0)
            {
                error = "Multi-frame DICOM requires SpacingBetweenSlices or SliceThickness in the managed v1 decoder.";
                return false;
            }
            var baseGeometry = new DicomImageGeometry(
                position,
                columnIndexDirection,
                rowIndexDirection,
                pixelSpacing[0],
                pixelSpacing[1],
                suggestedSpacing);

            var frames = new List<DicomImageFrame>(numberOfFrames);
            for (var frameIndex = 0; frameIndex < numberOfFrames; frameIndex++)
            {
                var framePixels = new int[pixelCountPerFrame];
                Array.Copy(allPixels, frameIndex * pixelCountPerFrame, framePixels, 0, pixelCountPerFrame);
                var framePosition = position + baseGeometry.SliceDirection * (frameIndex * suggestedSpacing);
                var frameGeometry = new DicomImageGeometry(
                    framePosition,
                    columnIndexDirection,
                    rowIndexDirection,
                    pixelSpacing[0],
                    pixelSpacing[1],
                    suggestedSpacing);
                frames.Add(new DicomImageFrame(
                    sopInstanceUid,
                    frameIndex + 1,
                    rows,
                    columns,
                    framePixels,
                    rescaleSlope,
                    rescaleIntercept,
                    windowCenter,
                    windowWidth,
                    photometric == "MONOCHROME1",
                    frameGeometry,
                    frameOfReferenceUid,
                    seriesInstanceUid));
            }

            decoded = new DicomDecodedFile(sourceName, sopInstanceUid, frames);
            return true;
        }

        static int[] DecodePixels(byte[] bytes, int bitsAllocated, int bitsStored, int highBit, int pixelRepresentation)
        {
            var bytesPerPixel = bitsAllocated / 8;
            var result = new int[bytes.Length / bytesPerPixel];
            var lowBit = highBit - bitsStored + 1;
            var mask = (1u << bitsStored) - 1u;
            var signBit = 1u << (bitsStored - 1);

            for (var i = 0; i < result.Length; i++)
            {
                uint allocatedValue = bytesPerPixel == 1
                    ? bytes[i]
                    : (uint)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                var storedValue = (allocatedValue >> lowBit) & mask;
                result[i] = pixelRepresentation == 1 && (storedValue & signBit) != 0
                    ? unchecked((int)(storedValue | ~mask))
                    : (int)storedValue;
            }
            return result;
        }

        static ElementHeader ReadElementHeader(Stream stream)
        {
            var group = ReadUInt16(stream);
            var element = ReadUInt16(stream);
            var vrBytes = ReadBytes(stream, 2);
            var vr = Encoding.ASCII.GetString(vrBytes);
            uint length;
            if (s_LongValueRepresentations.Contains(vr))
            {
                ReadUInt16(stream); // reserved
                length = ReadUInt32(stream);
            }
            else
            {
                length = ReadUInt16(stream);
            }
            return new ElementHeader(group, element, vr, length);
        }

        static void SkipUndefinedSequence(Stream stream)
        {
            while (true)
            {
                var group = ReadUInt16(stream);
                var element = ReadUInt16(stream);
                var length = ReadUInt32(stream);
                if (group == 0xfffe && element == 0xe0dd)
                {
                    if (length != 0)
                        throw new InvalidDataException("DICOM sequence delimiter has a non-zero length.");
                    return;
                }
                if (group != 0xfffe || element != 0xe000)
                    throw new InvalidDataException("Malformed undefined-length DICOM sequence.");

                if (length == UndefinedLength)
                    SkipUndefinedItem(stream);
                else
                    SkipBytes(stream, length);
            }
        }

        static void SkipUndefinedItem(Stream stream)
        {
            while (true)
            {
                var start = stream.Position;
                var group = ReadUInt16(stream);
                var element = ReadUInt16(stream);
                if (group == 0xfffe)
                {
                    var length = ReadUInt32(stream);
                    if (element == 0xe00d)
                    {
                        if (length != 0)
                            throw new InvalidDataException("DICOM item delimiter has a non-zero length.");
                        return;
                    }
                    throw new InvalidDataException("Unexpected DICOM delimiter inside an item.");
                }

                stream.Position = start;
                var header = ReadElementHeader(stream);
                if (header.Length == UndefinedLength)
                {
                    if (header.Vr != "SQ")
                        throw new InvalidDataException("Unsupported undefined-length value inside a DICOM item.");
                    SkipUndefinedSequence(stream);
                }
                else
                {
                    SkipBytes(stream, header.Length);
                }
            }
        }

        static string ReadLimitedText(Stream stream, ElementHeader header)
        {
            if (header.Length > MaximumMetadataValueBytes)
                throw new InvalidDataException($"DICOM tag ({header.Group:x4},{header.Element:x4}) is too large for a metadata value.");
            return ReadDicomText(stream, checked((int)header.Length));
        }

        static int ReadUnsignedShortValue(Stream stream, ElementHeader header)
        {
            if (header.Length != 2)
                throw new InvalidDataException($"DICOM tag ({header.Group:x4},{header.Element:x4}) must contain one US value.");
            return ReadUInt16(stream);
        }

        static string ReadDicomText(Stream stream, int length)
        {
            return Encoding.ASCII.GetString(ReadBytes(stream, length)).TrimEnd('\0', ' ');
        }

        static double[] ParseNumbers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<double>();
            var parts = text.Split('\\');
            var values = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                    throw new InvalidDataException($"Invalid DICOM decimal value '{parts[i]}'.");
            }
            return values;
        }

        static double FirstNumber(string text, double fallback)
        {
            var values = ParseNumbers(text);
            return values.Length == 0 ? fallback : values[0];
        }

        static int ParsePositiveInteger(string text, string fieldName)
        {
            var first = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Split('\\')[0].Trim();
            if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new InvalidDataException($"DICOM {fieldName} is not a positive integer.");
            return value;
        }

        static ushort ReadUInt16(Stream stream)
        {
            var low = stream.ReadByte();
            var high = stream.ReadByte();
            if (low < 0 || high < 0)
                throw new EndOfStreamException("DICOM element header is truncated.");
            return (ushort)(low | high << 8);
        }

        static uint ReadUInt32(Stream stream)
        {
            var bytes = ReadBytes(stream, 4);
            return (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
        }

        static byte[] ReadBytes(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("DICOM value is truncated.");
                offset += read;
            }
            return bytes;
        }

        static void SkipBytes(Stream stream, uint count)
        {
            if (count > long.MaxValue - stream.Position || stream.Position + count > stream.Length)
                throw new EndOfStreamException("DICOM value extends beyond the input stream.");
            stream.Position += count;
        }

        readonly struct ElementHeader
        {
            public readonly ushort Group;
            public readonly ushort Element;
            public readonly string Vr;
            public readonly uint Length;

            public ElementHeader(ushort group, ushort element, string vr, uint length)
            {
                Group = group;
                Element = element;
                Vr = vr;
                Length = length;
            }
        }
    }
}
