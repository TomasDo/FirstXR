using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public sealed class DicomVolume
    {
        readonly DicomImageFrame[] m_Frames;

        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }
        public double ColumnSpacingMm { get; }
        public double RowSpacingMm { get; }
        public double SliceSpacingMm { get; }
        public DicomVector3d OriginPatient { get; }
        public DicomVector3d ColumnIndexDirection { get; }
        public DicomVector3d RowIndexDirection { get; }
        public DicomVector3d SliceDirection { get; }
        public string FrameOfReferenceUid { get; }
        public double DefaultWindowCenter { get; }
        public double DefaultWindowWidth { get; }
        public bool IsMonochrome1 { get; }

        DicomVolume(
            int width,
            int height,
            int depth,
            double columnSpacingMm,
            double rowSpacingMm,
            double sliceSpacingMm,
            DicomVector3d originPatient,
            DicomVector3d columnIndexDirection,
            DicomVector3d rowIndexDirection,
            DicomVector3d sliceDirection,
            string frameOfReferenceUid,
            double defaultWindowCenter,
            double defaultWindowWidth,
            bool isMonochrome1,
            DicomImageFrame[] frames)
        {
            Width = width;
            Height = height;
            Depth = depth;
            ColumnSpacingMm = columnSpacingMm;
            RowSpacingMm = rowSpacingMm;
            SliceSpacingMm = sliceSpacingMm;
            OriginPatient = originPatient;
            ColumnIndexDirection = columnIndexDirection;
            RowIndexDirection = rowIndexDirection;
            SliceDirection = sliceDirection;
            FrameOfReferenceUid = frameOfReferenceUid ?? string.Empty;
            DefaultWindowCenter = defaultWindowCenter;
            DefaultWindowWidth = defaultWindowWidth;
            IsMonochrome1 = isMonochrome1;
            m_Frames = frames;
        }

        public static bool TryCreate(IEnumerable<DicomImageFrame> sourceFrames, out DicomVolume volume, out string error)
        {
            volume = null;
            error = string.Empty;
            if (sourceFrames == null)
            {
                error = "No DICOM frames were supplied.";
                return false;
            }

            var frames = sourceFrames.Where(frame => frame != null).ToList();
            if (frames.Count == 0)
            {
                error = "No DICOM image frames were supplied.";
                return false;
            }

            var first = frames[0];
            var referenceGeometry = first.Geometry;
            if (string.IsNullOrWhiteSpace(first.SopInstanceUid) || string.IsNullOrWhiteSpace(first.SeriesInstanceUid))
            {
                error = "DICOM frames require SOPInstanceUID and SeriesInstanceUID.";
                return false;
            }
            if (first.Rows <= 0 || first.Columns <= 0 || first.RawPixels.Length != first.Rows * first.Columns)
            {
                error = "The first DICOM frame has invalid pixel dimensions.";
                return false;
            }

            var frameIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var frame in frames)
            {
                if (string.IsNullOrWhiteSpace(frame.SopInstanceUid) ||
                    !frameIdentities.Add(frame.SopInstanceUid + "\n" + frame.FrameNumber.ToString()))
                {
                    error = "DICOM series contains a duplicate or missing SOP/frame identity.";
                    return false;
                }
                if (!string.Equals(frame.SeriesInstanceUid, first.SeriesInstanceUid, StringComparison.Ordinal))
                {
                    error = "DICOM volume contains more than one SeriesInstanceUID.";
                    return false;
                }
                if (frame.Rows != first.Rows || frame.Columns != first.Columns ||
                    frame.RawPixels.Length != first.Rows * first.Columns)
                {
                    error = "DICOM series contains frames with different pixel dimensions.";
                    return false;
                }
                if (Math.Abs(frame.Geometry.RowSpacingMm - referenceGeometry.RowSpacingMm) > 1e-4 ||
                    Math.Abs(frame.Geometry.ColumnSpacingMm - referenceGeometry.ColumnSpacingMm) > 1e-4)
                {
                    error = "DICOM series contains inconsistent PixelSpacing values.";
                    return false;
                }
                if (DicomVector3d.Dot(frame.Geometry.ColumnIndexDirection, referenceGeometry.ColumnIndexDirection) < 0.9999 ||
                    DicomVector3d.Dot(frame.Geometry.RowIndexDirection, referenceGeometry.RowIndexDirection) < 0.9999)
                {
                    error = "DICOM series contains inconsistent ImageOrientationPatient values.";
                    return false;
                }
                if (frame.IsMonochrome1 != first.IsMonochrome1)
                {
                    error = "DICOM series mixes MONOCHROME1 and MONOCHROME2 frames.";
                    return false;
                }
                if (!string.Equals(frame.FrameOfReferenceUid, first.FrameOfReferenceUid, StringComparison.Ordinal))
                {
                    error = "DICOM series contains more than one FrameOfReferenceUID.";
                    return false;
                }
            }

            frames.Sort((left, right) => SliceLocation(left, referenceGeometry.SliceDirection)
                .CompareTo(SliceLocation(right, referenceGeometry.SliceDirection)));

            var sliceSpacing = DetermineSliceSpacing(frames, referenceGeometry.SliceDirection, out error);
            if (sliceSpacing <= 0)
                return false;

            var firstPosition = frames[0].Geometry.ImagePositionPatient;
            var inPlaneTolerance = Math.Max(0.1, Math.Min(referenceGeometry.RowSpacingMm, referenceGeometry.ColumnSpacingMm) * 0.05);
            foreach (var frame in frames)
            {
                var positionDelta = frame.Geometry.ImagePositionPatient - firstPosition;
                if (Math.Abs(DicomVector3d.Dot(positionDelta, referenceGeometry.ColumnIndexDirection)) > inPlaneTolerance ||
                    Math.Abs(DicomVector3d.Dot(positionDelta, referenceGeometry.RowIndexDirection)) > inPlaneTolerance)
                {
                    error = "DICOM series has shifted slice origins (gantry tilt/sheared geometry is unsupported in v1).";
                    return false;
                }
            }

            var minValue = double.PositiveInfinity;
            var maxValue = double.NegativeInfinity;
            var pixelsPerFrame = first.Rows * first.Columns;
            for (var slice = 0; slice < frames.Count; slice++)
            {
                var frame = frames[slice];
                for (var pixel = 0; pixel < pixelsPerFrame; pixel++)
                {
                    var value = frame.RawPixels[pixel] * frame.RescaleSlope + frame.RescaleIntercept;
                    if (double.IsNaN(value) || double.IsInfinity(value) || value < float.MinValue || value > float.MaxValue)
                    {
                        error = "DICOM rescale produced a non-finite voxel value.";
                        return false;
                    }
                    minValue = Math.Min(minValue, value);
                    maxValue = Math.Max(maxValue, value);
                }
            }

            var windowCenter = first.WindowCenter ?? (minValue + maxValue) * 0.5;
            var windowWidth = first.WindowWidth.GetValueOrDefault(maxValue - minValue);
            if (windowWidth <= 0 || double.IsNaN(windowWidth) || double.IsInfinity(windowWidth))
                windowWidth = 1;

            volume = new DicomVolume(
                first.Columns,
                first.Rows,
                frames.Count,
                referenceGeometry.ColumnSpacingMm,
                referenceGeometry.RowSpacingMm,
                sliceSpacing,
                frames[0].Geometry.ImagePositionPatient,
                referenceGeometry.ColumnIndexDirection,
                referenceGeometry.RowIndexDirection,
                referenceGeometry.SliceDirection,
                first.FrameOfReferenceUid,
                windowCenter,
                windowWidth,
                first.IsMonochrome1,
                frames.ToArray());
            return true;
        }

        public float GetVoxel(int column, int row, int slice)
        {
            if (column < 0 || column >= Width)
                throw new ArgumentOutOfRangeException(nameof(column));
            if (row < 0 || row >= Height)
                throw new ArgumentOutOfRangeException(nameof(row));
            if (slice < 0 || slice >= Depth)
                throw new ArgumentOutOfRangeException(nameof(slice));
            var frame = m_Frames[slice];
            return (float)(frame.RawPixels[row * Width + column] * frame.RescaleSlope + frame.RescaleIntercept);
        }

        public DicomVector3d VoxelCenterPatient(double column, double row, double slice)
        {
            return OriginPatient +
                   ColumnIndexDirection * (column * ColumnSpacingMm) +
                   RowIndexDirection * (row * RowSpacingMm) +
                   SliceDirection * (slice * SliceSpacingMm);
        }

        public bool TryPatientToVoxel(DicomVector3d patientPoint, out double column, out double row, out double slice)
        {
            var relative = patientPoint - OriginPatient;
            column = DicomVector3d.Dot(relative, ColumnIndexDirection) / ColumnSpacingMm;
            row = DicomVector3d.Dot(relative, RowIndexDirection) / RowSpacingMm;
            slice = DicomVector3d.Dot(relative, SliceDirection) / SliceSpacingMm;
            return IsInsideVoxelAxis(column, Width) &&
                   IsInsideVoxelAxis(row, Height) &&
                   IsInsideVoxelAxis(slice, Depth);
        }

        public bool TrySamplePatientTrilinear(DicomVector3d patientPoint, out float value)
        {
            value = 0;
            if (!TryPatientToVoxel(patientPoint, out var x, out var y, out var z))
                return false;

            x = Math.Max(0, Math.Min(Width - 1, x));
            y = Math.Max(0, Math.Min(Height - 1, y));
            z = Math.Max(0, Math.Min(Depth - 1, z));

            var x0 = Math.Max(0, Math.Min(Width - 1, (int)Math.Floor(x)));
            var y0 = Math.Max(0, Math.Min(Height - 1, (int)Math.Floor(y)));
            var z0 = Math.Max(0, Math.Min(Depth - 1, (int)Math.Floor(z)));
            var x1 = Math.Min(Width - 1, x0 + 1);
            var y1 = Math.Min(Height - 1, y0 + 1);
            var z1 = Math.Min(Depth - 1, z0 + 1);
            var tx = x - x0;
            var ty = y - y0;
            var tz = z - z0;

            var c000 = GetVoxel(x0, y0, z0);
            var c100 = GetVoxel(x1, y0, z0);
            var c010 = GetVoxel(x0, y1, z0);
            var c110 = GetVoxel(x1, y1, z0);
            var c001 = GetVoxel(x0, y0, z1);
            var c101 = GetVoxel(x1, y0, z1);
            var c011 = GetVoxel(x0, y1, z1);
            var c111 = GetVoxel(x1, y1, z1);
            var c00 = Lerp(c000, c100, tx);
            var c10 = Lerp(c010, c110, tx);
            var c01 = Lerp(c001, c101, tx);
            var c11 = Lerp(c011, c111, tx);
            var c0 = Lerp(c00, c10, ty);
            var c1 = Lerp(c01, c11, ty);
            value = (float)Lerp(c0, c1, tz);
            return true;
        }

        static double Lerp(double start, double end, double amount) => start + (end - start) * amount;

        static bool IsInsideVoxelAxis(double coordinate, int count) =>
            count == 1 ? coordinate >= -0.5 && coordinate <= 0.5 : coordinate >= 0 && coordinate <= count - 1;

        static double SliceLocation(DicomImageFrame frame, DicomVector3d normal) =>
            DicomVector3d.Dot(frame.Geometry.ImagePositionPatient, normal);

        static double DetermineSliceSpacing(List<DicomImageFrame> frames, DicomVector3d normal, out string error)
        {
            error = string.Empty;
            if (frames.Count == 1)
                return frames[0].Geometry.SuggestedSliceSpacingMm > 0
                    ? frames[0].Geometry.SuggestedSliceSpacingMm
                    : 1.0;

            var spacings = new List<double>(frames.Count - 1);
            for (var i = 1; i < frames.Count; i++)
            {
                var spacing = SliceLocation(frames[i], normal) - SliceLocation(frames[i - 1], normal);
                if (spacing <= 1e-5)
                {
                    error = "DICOM series contains duplicate or reversed slice locations.";
                    return 0;
                }
                spacings.Add(spacing);
            }

            spacings.Sort();
            var median = spacings[spacings.Count / 2];
            var tolerance = Math.Max(0.1, median * 0.05);
            if (spacings.Any(value => Math.Abs(value - median) > tolerance))
            {
                error = "DICOM series has irregular slice spacing beyond the supported tolerance.";
                return 0;
            }
            return median;
        }
    }

    public readonly struct DicomSliceRequest : IEquatable<DicomSliceRequest>
    {
        public readonly DicomVector3d TopLeftPatient;
        public readonly DicomVector3d HorizontalDirection;
        public readonly DicomVector3d VerticalDirection;
        public readonly double WidthMm;
        public readonly double HeightMm;
        public readonly int PixelWidth;
        public readonly int PixelHeight;
        public readonly double WindowCenter;
        public readonly double WindowWidth;

        public DicomSliceRequest(
            DicomVector3d topLeftPatient,
            DicomVector3d horizontalDirection,
            DicomVector3d verticalDirection,
            double widthMm,
            double heightMm,
            int pixelWidth,
            int pixelHeight,
            double windowCenter,
            double windowWidth)
        {
            if (widthMm < 0 || heightMm < 0)
                throw new ArgumentOutOfRangeException(nameof(widthMm));
            if (pixelWidth <= 0 || pixelHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelWidth));
            if (windowWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowWidth));
            TopLeftPatient = topLeftPatient;
            HorizontalDirection = horizontalDirection.Normalized;
            VerticalDirection = verticalDirection.Normalized;
            if (Math.Abs(DicomVector3d.Dot(HorizontalDirection, VerticalDirection)) > 1e-4)
                throw new ArgumentException("Slice horizontal and vertical directions must be orthogonal.");
            WidthMm = widthMm;
            HeightMm = heightMm;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            WindowCenter = windowCenter;
            WindowWidth = windowWidth;
        }

        public static DicomSliceRequest Native(DicomVolume volume, int sliceIndex, double? windowCenter = null, double? windowWidth = null)
        {
            if (volume == null)
                throw new ArgumentNullException(nameof(volume));
            if (sliceIndex < 0 || sliceIndex >= volume.Depth)
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            return new DicomSliceRequest(
                volume.VoxelCenterPatient(0, 0, sliceIndex),
                volume.ColumnIndexDirection,
                volume.RowIndexDirection,
                Math.Max(0, volume.Width - 1) * volume.ColumnSpacingMm,
                Math.Max(0, volume.Height - 1) * volume.RowSpacingMm,
                volume.Width,
                volume.Height,
                windowCenter ?? volume.DefaultWindowCenter,
                windowWidth ?? volume.DefaultWindowWidth);
        }

        public bool Equals(DicomSliceRequest other)
        {
            return TopLeftPatient.Equals(other.TopLeftPatient) &&
                   HorizontalDirection.Equals(other.HorizontalDirection) &&
                   VerticalDirection.Equals(other.VerticalDirection) &&
                   WidthMm.Equals(other.WidthMm) && HeightMm.Equals(other.HeightMm) &&
                   PixelWidth == other.PixelWidth && PixelHeight == other.PixelHeight &&
                   WindowCenter.Equals(other.WindowCenter) && WindowWidth.Equals(other.WindowWidth);
        }

        public override bool Equals(object obj) => obj is DicomSliceRequest other && Equals(other);
        public override int GetHashCode() =>
            (TopLeftPatient, HorizontalDirection, VerticalDirection, WidthMm, HeightMm, PixelWidth, PixelHeight, WindowCenter, WindowWidth).GetHashCode();
    }

    public readonly struct DicomPatientPlane
    {
        public readonly DicomSliceRequest Request;
        public readonly double StepMm;

        public DicomPatientPlane(DicomSliceRequest request, double stepMm)
        {
            Request = request;
            StepMm = stepMm;
        }
    }

    public static class DicomPatientPlaneFactory
    {
        /// <summary>
        /// Builds a viewport for the protocol convention: buccalUp is screen up, and
        /// Cross(normal, buccalUp) is screen-left/mesial. The output conservatively covers
        /// the physical volume bounds and rejects planes that do not intersect the volume.
        /// </summary>
        public static bool TryCreate(
            DicomVolume volume,
            DicomVector3d originMm,
            DicomVector3d normal,
            DicomVector3d buccalUp,
            double offsetMm,
            int maximumResolution,
            out DicomPatientPlane plane,
            out string error)
        {
            plane = default;
            error = string.Empty;
            if (volume == null)
            {
                error = "CT volume is missing.";
                return false;
            }
            if (!IsFinite(originMm) || !IsFinite(normal) || !IsFinite(buccalUp) ||
                double.IsNaN(offsetMm) || double.IsInfinity(offsetMm))
            {
                error = "Patient-space slice contains a non-finite value.";
                return false;
            }
            if (normal.Magnitude < 1e-8 || buccalUp.Magnitude < 1e-8)
            {
                error = "Patient-space slice normal and buccal-up direction must be non-zero.";
                return false;
            }
            if (maximumResolution < 1)
            {
                error = "Patient-space slice maximum resolution must be positive.";
                return false;
            }

            normal = normal.Normalized;
            buccalUp = buccalUp.Normalized;
            var normalUpDot = DicomVector3d.Dot(normal, buccalUp);
            if (Math.Abs(normalUpDot) > 0.01)
            {
                error = "Patient-space slice normal must be orthogonal to buccal-up.";
                return false;
            }
            buccalUp = (buccalUp - normal * normalUpDot).Normalized;
            var mesialLeft = DicomVector3d.Cross(normal, buccalUp).Normalized;
            var screenRight = mesialLeft * -1;
            var screenDown = buccalUp * -1;
            var center = originMm + normal * offsetMm;

            var minNormal = double.PositiveInfinity;
            var maxNormal = double.NegativeInfinity;
            var minHorizontal = double.PositiveInfinity;
            var maxHorizontal = double.NegativeInfinity;
            var minVertical = double.PositiveInfinity;
            var maxVertical = double.NegativeInfinity;
            foreach (var corner in GetVolumeCorners(volume))
            {
                var relative = corner - center;
                var normalDistance = DicomVector3d.Dot(relative, normal);
                var horizontal = DicomVector3d.Dot(relative, screenRight);
                var vertical = DicomVector3d.Dot(relative, screenDown);
                minNormal = Math.Min(minNormal, normalDistance);
                maxNormal = Math.Max(maxNormal, normalDistance);
                minHorizontal = Math.Min(minHorizontal, horizontal);
                maxHorizontal = Math.Max(maxHorizontal, horizontal);
                minVertical = Math.Min(minVertical, vertical);
                maxVertical = Math.Max(maxVertical, vertical);
            }

            var sampleSpacing = Math.Min(volume.ColumnSpacingMm, Math.Min(volume.RowSpacingMm, volume.SliceSpacingMm));
            var halfVoxelTolerance = Math.Max(volume.SliceSpacingMm, Math.Max(volume.RowSpacingMm, volume.ColumnSpacingMm)) * 0.5;
            if (minNormal > halfVoxelTolerance || maxNormal < -halfVoxelTolerance)
            {
                error = "Patient-space slice does not intersect the CT volume.";
                return false;
            }

            var widthMm = Math.Max(0, maxHorizontal - minHorizontal);
            var heightMm = Math.Max(0, maxVertical - minVertical);
            var requestedWidth = Math.Ceiling(widthMm / sampleSpacing) + 1;
            var requestedHeight = Math.Ceiling(heightMm / sampleSpacing) + 1;
            var pixelWidth = requestedWidth >= maximumResolution ? maximumResolution : Math.Max(1, (int)requestedWidth);
            var pixelHeight = requestedHeight >= maximumResolution ? maximumResolution : Math.Max(1, (int)requestedHeight);
            var topLeft = center + screenRight * minHorizontal + screenDown * minVertical;
            var request = new DicomSliceRequest(
                topLeft,
                screenRight,
                screenDown,
                widthMm,
                heightMm,
                pixelWidth,
                pixelHeight,
                volume.DefaultWindowCenter,
                volume.DefaultWindowWidth);
            plane = new DicomPatientPlane(request, sampleSpacing);
            return true;
        }

        static DicomVector3d[] GetVolumeCorners(DicomVolume volume)
        {
            var maxColumn = Math.Max(0, volume.Width - 1);
            var maxRow = Math.Max(0, volume.Height - 1);
            var maxSlice = Math.Max(0, volume.Depth - 1);
            return new[]
            {
                volume.VoxelCenterPatient(0, 0, 0),
                volume.VoxelCenterPatient(maxColumn, 0, 0),
                volume.VoxelCenterPatient(0, maxRow, 0),
                volume.VoxelCenterPatient(maxColumn, maxRow, 0),
                volume.VoxelCenterPatient(0, 0, maxSlice),
                volume.VoxelCenterPatient(maxColumn, 0, maxSlice),
                volume.VoxelCenterPatient(0, maxRow, maxSlice),
                volume.VoxelCenterPatient(maxColumn, maxRow, maxSlice),
            };
        }

        static bool IsFinite(DicomVector3d value) =>
            !double.IsNaN(value.X) && !double.IsInfinity(value.X) &&
            !double.IsNaN(value.Y) && !double.IsInfinity(value.Y) &&
            !double.IsNaN(value.Z) && !double.IsInfinity(value.Z);
    }

    public sealed class DicomCpuSliceCache
    {
        readonly int m_Capacity;
        readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> m_Entries =
            new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
        readonly LinkedList<CacheEntry> m_LeastRecentlyUsed = new LinkedList<CacheEntry>();

        public DicomCpuSliceCache(int capacity = 8)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Capacity = capacity;
        }

        public byte[] GetOrCreate(DicomVolume volume, DicomSliceRequest request)
        {
            return GetOrCreate(volume, request, null);
        }

        public byte[] GetOrCreate(DicomVolume volume, DicomSliceRequest request, Func<bool> isStale)
        {
            if (volume == null)
                throw new ArgumentNullException(nameof(volume));
            if (isStale != null && isStale())
                throw new OperationCanceledException("DICOM slice request was superseded.");
            var key = new CacheKey(volume, request);
            if (m_Entries.TryGetValue(key, out var existing))
            {
                m_LeastRecentlyUsed.Remove(existing);
                m_LeastRecentlyUsed.AddFirst(existing);
                return existing.Value.Pixels;
            }

            var pixels = Render(volume, request, isStale);
            var entry = new CacheEntry(key, pixels);
            var node = m_LeastRecentlyUsed.AddFirst(entry);
            m_Entries.Add(key, node);
            while (m_Entries.Count > m_Capacity)
            {
                var last = m_LeastRecentlyUsed.Last;
                m_LeastRecentlyUsed.RemoveLast();
                m_Entries.Remove(last.Value.Key);
            }
            return pixels;
        }

        public void Clear()
        {
            m_Entries.Clear();
            m_LeastRecentlyUsed.Clear();
        }

        static byte[] Render(DicomVolume volume, DicomSliceRequest request, Func<bool> isStale)
        {
            var pixels = new byte[checked(request.PixelWidth * request.PixelHeight)];
            for (var row = 0; row < request.PixelHeight; row++)
            {
                if (isStale != null && isStale())
                    throw new OperationCanceledException("DICOM slice request was superseded.");
                var verticalMm = request.PixelHeight == 1 ? 0 : request.HeightMm * row / (request.PixelHeight - 1);
                for (var column = 0; column < request.PixelWidth; column++)
                {
                    var horizontalMm = request.PixelWidth == 1 ? 0 : request.WidthMm * column / (request.PixelWidth - 1);
                    var point = request.TopLeftPatient +
                                request.HorizontalDirection * horizontalMm +
                                request.VerticalDirection * verticalMm;
                    if (!volume.TrySamplePatientTrilinear(point, out var value))
                    {
                        pixels[row * request.PixelWidth + column] = 0;
                        continue;
                    }

                    var normalized = request.WindowWidth <= 1
                        ? (value > request.WindowCenter - 0.5 ? 1.0 : 0.0)
                        : ((value - (request.WindowCenter - 0.5)) / (request.WindowWidth - 1.0) + 0.5);
                    normalized = Math.Max(0, Math.Min(1, normalized));
                    if (volume.IsMonochrome1)
                        normalized = 1 - normalized;
                    pixels[row * request.PixelWidth + column] = (byte)Math.Round(normalized * 255, MidpointRounding.AwayFromZero);
                }
            }
            return pixels;
        }

        readonly struct CacheKey : IEquatable<CacheKey>
        {
            readonly DicomVolume m_Volume;
            readonly DicomSliceRequest m_Request;

            public CacheKey(DicomVolume volume, DicomSliceRequest request)
            {
                m_Volume = volume;
                m_Request = request;
            }

            public bool Equals(CacheKey other) => ReferenceEquals(m_Volume, other.m_Volume) && m_Request.Equals(other.m_Request);
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => (m_Volume, m_Request).GetHashCode();
        }

        sealed class CacheEntry
        {
            public readonly CacheKey Key;
            public readonly byte[] Pixels;

            public CacheEntry(CacheKey key, byte[] pixels)
            {
                Key = key;
                Pixels = pixels;
            }
        }
    }

    public sealed class DicomSliceTextureCache : IDisposable
    {
        readonly int m_Capacity;
        readonly DicomCpuSliceCache m_CpuCache;
        readonly Dictionary<TextureKey, LinkedListNode<TextureEntry>> m_Entries =
            new Dictionary<TextureKey, LinkedListNode<TextureEntry>>();
        readonly LinkedList<TextureEntry> m_LeastRecentlyUsed = new LinkedList<TextureEntry>();

        public DicomSliceTextureCache(int capacity = 8)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Capacity = capacity;
            m_CpuCache = new DicomCpuSliceCache(capacity);
        }

        public Texture2D GetOrCreate(DicomVolume volume, DicomSliceRequest request)
        {
            if (TryGet(volume, request, out var existing))
                return existing;

            var grayscale = m_CpuCache.GetOrCreate(volume, request);
            return GetOrCreateFromPixels(volume, request, grayscale);
        }

        public bool TryGet(DicomVolume volume, DicomSliceRequest request, out Texture2D texture)
        {
            var key = new TextureKey(volume, request);
            if (m_Entries.TryGetValue(key, out var existing))
            {
                m_LeastRecentlyUsed.Remove(existing);
                m_LeastRecentlyUsed.AddFirst(existing);
                texture = existing.Value.Texture;
                return true;
            }
            texture = null;
            return false;
        }

        /// <summary>Creates and uploads a Unity texture. Call only from Unity's main thread.</summary>
        public Texture2D GetOrCreateFromPixels(DicomVolume volume, DicomSliceRequest request, byte[] grayscale)
        {
            if (TryGet(volume, request, out var existing))
                return existing;
            if (grayscale == null || grayscale.Length != checked(request.PixelWidth * request.PixelHeight))
                throw new ArgumentException("Slice pixels do not match the requested dimensions.", nameof(grayscale));

            var key = new TextureKey(volume, request);
            var rgba = new byte[checked(grayscale.Length * 4)];
            for (var sourceRow = 0; sourceRow < request.PixelHeight; sourceRow++)
            {
                var destinationRow = request.PixelHeight - sourceRow - 1;
                for (var column = 0; column < request.PixelWidth; column++)
                {
                    var gray = grayscale[sourceRow * request.PixelWidth + column];
                    var destination = (destinationRow * request.PixelWidth + column) * 4;
                    rgba[destination] = gray;
                    rgba[destination + 1] = gray;
                    rgba[destination + 2] = gray;
                    rgba[destination + 3] = 255;
                }
            }

            var texture = new Texture2D(request.PixelWidth, request.PixelHeight, TextureFormat.RGBA32, false, true)
            {
                name = "Dental CT Slice",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, true);

            var entry = new TextureEntry(key, texture);
            var node = m_LeastRecentlyUsed.AddFirst(entry);
            m_Entries.Add(key, node);
            while (m_Entries.Count > m_Capacity)
            {
                var last = m_LeastRecentlyUsed.Last;
                m_LeastRecentlyUsed.RemoveLast();
                m_Entries.Remove(last.Value.Key);
                DestroyTexture(last.Value.Texture);
            }
            return texture;
        }

        public void Clear()
        {
            foreach (var entry in m_Entries.Values)
                DestroyTexture(entry.Value.Texture);
            m_Entries.Clear();
            m_LeastRecentlyUsed.Clear();
            m_CpuCache.Clear();
        }

        public void Dispose() => Clear();

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(texture);
            else
#endif
                UnityEngine.Object.Destroy(texture);
        }

        readonly struct TextureKey : IEquatable<TextureKey>
        {
            readonly DicomVolume m_Volume;
            readonly DicomSliceRequest m_Request;

            public TextureKey(DicomVolume volume, DicomSliceRequest request)
            {
                m_Volume = volume;
                m_Request = request;
            }

            public bool Equals(TextureKey other) => ReferenceEquals(m_Volume, other.m_Volume) && m_Request.Equals(other.m_Request);
            public override bool Equals(object obj) => obj is TextureKey other && Equals(other);
            public override int GetHashCode() => (m_Volume, m_Request).GetHashCode();
        }

        sealed class TextureEntry
        {
            public readonly TextureKey Key;
            public readonly Texture2D Texture;

            public TextureEntry(TextureKey key, Texture2D texture)
            {
                Key = key;
                Texture = texture;
            }
        }
    }
}
