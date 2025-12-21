#!/bin/bash
# Script to verify PortAudio libraries are present for all platforms
# This script is used by CI to ensure all required libraries exist

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNTIMES_DIR="$SCRIPT_DIR/runtimes"

echo "Checking PortAudio native libraries..."
echo

missing_libs=()

# Check Windows libraries
for arch in x64 x86 arm64; do
    lib_path="$RUNTIMES_DIR/win-$arch/native/libportaudio.dll"
    if [ ! -f "$lib_path" ]; then
        echo "❌ Missing: $lib_path"
        missing_libs+=("win-$arch")
    else
        echo "✓ Found: $lib_path ($(du -h "$lib_path" | cut -f1))"
    fi
done

# Check macOS libraries
for arch in x64 arm64; do
    lib_path="$RUNTIMES_DIR/osx-$arch/native/libportaudio.2.dylib"
    if [ ! -f "$lib_path" ]; then
        echo "❌ Missing: $lib_path"
        missing_libs+=("osx-$arch")
    else
        echo "✓ Found: $lib_path ($(du -h "$lib_path" | cut -f1))"
    fi
done

# Check Linux libraries
for arch in x64 arm64; do
    lib_path="$RUNTIMES_DIR/linux-$arch/native/libportaudio.so.2"
    if [ ! -f "$lib_path" ]; then
        echo "❌ Missing: $lib_path"
        missing_libs+=("linux-$arch")
    else
        echo "✓ Found: $lib_path ($(du -h "$lib_path" | cut -f1))"
    fi
done

echo
if [ ${#missing_libs[@]} -eq 0 ]; then
    echo "✅ All PortAudio libraries are present!"
    exit 0
else
    echo "⚠️  Missing libraries for: ${missing_libs[*]}"
    echo
    echo "To build missing libraries:"
    echo "  1. Run the build-portaudio.yml GitHub Actions workflow"
    echo "  2. Download the artifacts and place them in the runtimes directory"
    echo "  3. Or build manually using CMake (see runtimes/README.md)"
    echo
    echo "Note: The package will still build, but will only include available libraries."
    exit 0
fi
