#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(git -C "$script_dir" rev-parse --show-toplevel)"
dcmtk_dir="${1:-}"

if [[ -z "$dcmtk_dir" ]]; then
    echo "Usage: $0 /absolute/path/to/dcmtk-3.6.8-install/lib/cmake/dcmtk" >&2
    exit 2
fi

unity_ndk="/Applications/Unity/Hub/Editor/2022.3.62f3c1/PlaybackEngines/AndroidPlayer/NDK"
android_ndk="${ANDROID_NDK_HOME:-$unity_ndk}"
cmake_command="${CMAKE_COMMAND:-/Applications/CMake.app/Contents/bin/cmake}"
build_dir="${DENTAL_DCMTK_BUILD_DIR:-$script_dir/build/android-arm64}"
output_dir="$repo_root/Assets/Plugins/Android/arm64-v8a"

if [[ ! -f "$android_ndk/build/cmake/android.toolchain.cmake" ]]; then
    echo "Android NDK toolchain not found: $android_ndk" >&2
    exit 3
fi
if [[ ! -x "$cmake_command" ]]; then
    echo "CMake not found: $cmake_command" >&2
    exit 4
fi

mkdir -p "$output_dir"
"$cmake_command" -S "$script_dir" -B "$build_dir" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE="$android_ndk/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM=android-29 \
    -DANDROID_STL=c++_shared \
    -DDCMTK_DIR="$dcmtk_dir" \
    -DDENTAL_DCMTK_OUTPUT_DIRECTORY="$output_dir"
"$cmake_command" --build "$build_dir" --target dental_dcmtk

echo "Built $output_dir/libdental_dcmtk.so"
