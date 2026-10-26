# Android DCMTK decoder bridge

This directory is ignored by Unity because its name ends in `~`. It contains the source used to
produce the Android `libdental_dcmtk.so` consumed by `DcmtkAndroidDicomDecoder`.

## Fixed dependency and ABI

- DCMTK is pinned to **3.6.8**. CMake rejects another package version, and the managed adapter checks
  `dental_dcmtk_version()` at runtime before selecting the native decoder.
- Target ABI: `arm64-v8a`, Android API 29, matching this Unity project's Android settings.
- Supported v1 input: DICOM Part 10, uncompressed Explicit VR Little Endian
  (`1.2.840.10008.1.2.1`), 8/16-bit integer MONOCHROME1 or MONOCHROME2 pixels. Classic multi-frame
  files require a positive spacing tag. Compressed transfer syntaxes and enhanced per-frame geometry
  are rejected instead of being interpreted approximately.
- The bridge extracts every frame, core CT pixel metadata, Image Position (Patient), Image
  Orientation (Patient), Pixel Spacing, Frame of Reference UID, Series Instance UID, rescale, and
  display-window values. It verifies Pixel Data contains the declared rows × columns × frames before
  exposing any frame buffer to managed code.

## Build

First cross-compile and install the official DCMTK 3.6.8 source tree for `arm64-v8a` with the Unity
2022.3.62f3c1 NDK, Android API 29, C++ runtime `c++_shared`, and position-independent code. The
installed package must include its CMake config and the `dcmdata`, `oflog`, and `ofstd` targets.
Only uncompressed transfer syntax is used by this bridge, so optional image-codec dependencies may
be disabled in that DCMTK build.

Then run:

```sh
chmod +x build-android-arm64.sh
./build-android-arm64.sh /absolute/path/to/dcmtk-3.6.8-install/lib/cmake/dcmtk
```

Set `ANDROID_NDK_HOME` or `CMAKE_COMMAND` to override the Unity NDK and CMake paths. The script writes
`Assets/Plugins/Android/arm64-v8a/libdental_dcmtk.so`, which Unity packages for Android. Keep any
required DCMTK shared dependencies beside it; a static DCMTK install avoids those extra runtime
libraries.

The repository intentionally does not vendor DCMTK source or binaries. Until a 3.6.8 Android install
is supplied, the Android native build and Beam Pro runtime validation remain incomplete. In that
case `DentalCtVolumeService` selects the actual managed Explicit VR Little Endian decoder as a
functional fallback; that fallback is not evidence that the DCMTK path has been built or tested.
