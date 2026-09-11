using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// IDicomDecoder adapter for the arm64-v8a libdental_dcmtk.so bridge. The native bridge is
    /// pinned to DCMTK 3.6.8; when it is absent the service uses the managed Explicit VR decoder.
    /// </summary>
    public sealed class DcmtkAndroidDicomDecoder : IDicomDecoder
    {
        public const string RequiredDcmtkVersion = "3.6.8";
        const string NativeLibrary = "dental_dcmtk";
        const int ErrorCapacity = 1024;
        const int StringCapacity = 1024;

        enum IntegerField
        {
            Rows = 1,
            Columns = 2,
            FrameCount = 3,
            IsMonochrome1 = 4,
        }

        enum DoubleField
        {
            ImagePositionPatient = 1,
            ImageOrientationPatient = 2,
            PixelSpacing = 3,
            SliceSpacing = 4,
            RescaleSlope = 5,
            RescaleIntercept = 6,
            WindowCenter = 7,
            WindowWidth = 8,
        }

        enum StringField
        {
            SopInstanceUid = 1,
            FrameOfReferenceUid = 2,
            SeriesInstanceUid = 3,
        }

        public bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    var versionPointer = NativeMethods.Version();
                    var version = versionPointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(versionPointer);
                    return string.Equals(version, RequiredDcmtkVersion, StringComparison.Ordinal);
                }
                catch (Exception exception) when (IsNativeLoadFailure(exception))
                {
                    return false;
                }
#else
                return false;
#endif
            }
        }

        public bool TryDecode(string filePath, out DicomDecodedFile decoded, out string error)
        {
            decoded = null;
            error = string.Empty;
#if !UNITY_ANDROID || UNITY_EDITOR
            error = "DCMTK Android decoder is only available in an Android player.";
            return false;
#else
            if (!IsAvailable)
            {
                error = $"libdental_dcmtk.so with DCMTK {RequiredDcmtkVersion} is unavailable.";
                return false;
            }

            var errorBuffer = new StringBuilder(ErrorCapacity);
            IntPtr handle = IntPtr.Zero;
            try
            {
                if (NativeMethods.Open(filePath, out handle, errorBuffer, errorBuffer.Capacity) != 0 || handle == IntPtr.Zero)
                {
                    error = errorBuffer.Length == 0 ? "DCMTK could not open the DICOM file." : errorBuffer.ToString();
                    return false;
                }

                var rows = NativeMethods.GetInt(handle, (int)IntegerField.Rows);
                var columns = NativeMethods.GetInt(handle, (int)IntegerField.Columns);
                var frameCount = NativeMethods.GetInt(handle, (int)IntegerField.FrameCount);
                if (rows <= 0 || columns <= 0 || frameCount <= 0)
                {
                    error = "DCMTK bridge returned invalid image dimensions.";
                    return false;
                }

                int pixelsPerFrame;
                try
                {
                    pixelsPerFrame = checked(rows * columns);
                }
                catch (OverflowException)
                {
                    error = "DCMTK image dimensions exceed the managed array limit.";
                    return false;
                }

                var position = ReadVector(handle, DoubleField.ImagePositionPatient, 3);
                var orientation = ReadVector(handle, DoubleField.ImageOrientationPatient, 6);
                var spacing = ReadVector(handle, DoubleField.PixelSpacing, 2);
                var sliceSpacing = NativeMethods.GetDouble(handle, (int)DoubleField.SliceSpacing, 0);
                var slope = NativeMethods.GetDouble(handle, (int)DoubleField.RescaleSlope, 0);
                var intercept = NativeMethods.GetDouble(handle, (int)DoubleField.RescaleIntercept, 0);
                var center = NativeMethods.GetDouble(handle, (int)DoubleField.WindowCenter, 0);
                var width = NativeMethods.GetDouble(handle, (int)DoubleField.WindowWidth, 0);
                var sopInstanceUid = ReadString(handle, StringField.SopInstanceUid);
                var frameOfReferenceUid = ReadString(handle, StringField.FrameOfReferenceUid);
                var seriesInstanceUid = ReadString(handle, StringField.SeriesInstanceUid);
                var columnDirection = new DicomVector3d(orientation[0], orientation[1], orientation[2]);
                var rowDirection = new DicomVector3d(orientation[3], orientation[4], orientation[5]);
                var basePosition = new DicomVector3d(position[0], position[1], position[2]);
                var baseGeometry = new DicomImageGeometry(
                    basePosition,
                    columnDirection,
                    rowDirection,
                    spacing[0],
                    spacing[1],
                    sliceSpacing);

                var frames = new List<DicomImageFrame>(frameCount);
                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    var pixels = new int[pixelsPerFrame];
                    if (NativeMethods.CopyFramePixels(handle, frameIndex, pixels, pixels.Length) != pixels.Length)
                    {
                        error = $"DCMTK bridge could not decode frame {frameIndex + 1}.";
                        return false;
                    }

                    var framePosition = basePosition + baseGeometry.SliceDirection * (frameIndex * sliceSpacing);
                    var geometry = new DicomImageGeometry(
                        framePosition,
                        columnDirection,
                        rowDirection,
                        spacing[0],
                        spacing[1],
                        sliceSpacing);
                    frames.Add(new DicomImageFrame(
                        sopInstanceUid,
                        frameIndex + 1,
                        rows,
                        columns,
                        pixels,
                        IsFinite(slope) && Math.Abs(slope) > double.Epsilon ? slope : 1,
                        IsFinite(intercept) ? intercept : 0,
                        IsFinite(center) ? center : (double?)null,
                        IsFinite(width) && width > 0 ? width : (double?)null,
                        NativeMethods.GetInt(handle, (int)IntegerField.IsMonochrome1) != 0,
                        geometry,
                        frameOfReferenceUid,
                        seriesInstanceUid));
                }

                decoded = new DicomDecodedFile(filePath, sopInstanceUid, frames);
                return true;
            }
            catch (Exception exception) when (
                IsNativeLoadFailure(exception) ||
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is OverflowException)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    NativeMethods.Close(handle);
            }
#endif
        }

        static double[] ReadVector(IntPtr handle, DoubleField field, int count)
        {
            var values = new double[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = NativeMethods.GetDouble(handle, (int)field, index);
                if (!IsFinite(values[index]))
                    throw new InvalidOperationException($"DCMTK bridge returned invalid {field} metadata.");
            }
            return values;
        }

        static string ReadString(IntPtr handle, StringField field)
        {
            var buffer = new StringBuilder(StringCapacity);
            var length = NativeMethods.CopyString(handle, (int)field, buffer, buffer.Capacity);
            if (length < 0)
                throw new InvalidOperationException($"DCMTK bridge could not read {field}.");
            return buffer.ToString();
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        static bool IsNativeLoadFailure(Exception exception) =>
            exception is DllNotFoundException ||
            exception is EntryPointNotFoundException ||
            exception is BadImageFormatException;

        static class NativeMethods
        {
            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_version", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr Version();

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_open", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern int Open(string path, out IntPtr handle, StringBuilder error, int errorCapacity);

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_get_int", CallingConvention = CallingConvention.Cdecl)]
            public static extern int GetInt(IntPtr handle, int field);

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_get_double", CallingConvention = CallingConvention.Cdecl)]
            public static extern double GetDouble(IntPtr handle, int field, int index);

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_copy_string", CallingConvention = CallingConvention.Cdecl)]
            public static extern int CopyString(IntPtr handle, int field, StringBuilder destination, int capacity);

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_copy_frame_pixels_i32", CallingConvention = CallingConvention.Cdecl)]
            public static extern int CopyFramePixels(IntPtr handle, int frameIndex, [Out] int[] destination, int pixelCapacity);

            [DllImport(NativeLibrary, EntryPoint = "dental_dcmtk_close", CallingConvention = CallingConvention.Cdecl)]
            public static extern void Close(IntPtr handle);
        }
    }
}
