#!/bin/bash
set -e

# Script to build PortAudio locally for development/debugging
# This builds PortAudio for the current platform only

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PORTAUDIO_VERSION="v19.7.0"
BUILD_DIR="$SCRIPT_DIR/build/portaudio"
INSTALL_DIR="$SCRIPT_DIR/src/Bufdio.Spice86/runtimes"

echo "Building PortAudio $PORTAUDIO_VERSION for local development..."

# Detect platform
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    PLATFORM="linux"
    ARCH=$(uname -m)
    if [ "$ARCH" == "x86_64" ]; then
        RID="linux-x64"
        LIB_NAME="libportaudio.so.2"
    elif [ "$ARCH" == "aarch64" ]; then
        RID="linux-arm64"
        LIB_NAME="libportaudio.so.2"
    else
        echo "Unsupported Linux architecture: $ARCH"
        exit 1
    fi
elif [[ "$OSTYPE" == "darwin"* ]]; then
    PLATFORM="macos"
    ARCH=$(uname -m)
    if [ "$ARCH" == "x86_64" ]; then
        RID="osx-x64"
    elif [ "$ARCH" == "arm64" ]; then
        RID="osx-arm64"
    else
        echo "Unsupported macOS architecture: $ARCH"
        exit 1
    fi
    LIB_NAME="libportaudio.2.dylib"
elif [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" || "$OSTYPE" == "win32" ]]; then
    PLATFORM="windows"
    # Assume x64 for Windows
    RID="win-x64"
    LIB_NAME="libportaudio.dll"
else
    echo "Unsupported platform: $OSTYPE"
    exit 1
fi

echo "Platform: $PLATFORM"
echo "RID: $RID"
echo "Library: $LIB_NAME"

# Check dependencies
if ! command -v cmake &> /dev/null; then
    echo "Error: CMake is not installed. Please install CMake first."
    exit 1
fi

if ! command -v git &> /dev/null; then
    echo "Error: Git is not installed. Please install Git first."
    exit 1
fi

# Install platform-specific dependencies
if [ "$PLATFORM" == "linux" ]; then
    echo "Checking for ALSA development libraries..."
    if ! pkg-config --exists alsa; then
        echo "Error: ALSA development libraries not found."
        echo "Please install them with: sudo apt-get install libasound2-dev"
        exit 1
    fi
fi

# Clone PortAudio if not already present
if [ ! -d "$BUILD_DIR" ]; then
    echo "Cloning PortAudio repository..."
    mkdir -p "$(dirname "$BUILD_DIR")"
    git clone --depth 1 --branch "$PORTAUDIO_VERSION" https://github.com/PortAudio/portaudio.git "$BUILD_DIR"
fi

cd "$BUILD_DIR"

# Configure CMake based on platform
echo "Configuring CMake..."
if [ "$PLATFORM" == "linux" ]; then
    cmake -B build -S . \
        -DCMAKE_BUILD_TYPE=Release \
        -DPA_BUILD_SHARED=ON \
        -DPA_BUILD_STATIC=OFF \
        -DPA_USE_ALSA=ON \
        -DPA_USE_JACK=OFF \
        -DPA_USE_OSS=OFF \
        -DCMAKE_INSTALL_PREFIX=install
elif [ "$PLATFORM" == "macos" ]; then
    cmake -B build -S . \
        -DCMAKE_BUILD_TYPE=Release \
        -DPA_BUILD_SHARED=ON \
        -DPA_BUILD_STATIC=OFF \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DCMAKE_INSTALL_PREFIX=install
elif [ "$PLATFORM" == "windows" ]; then
    cmake -B build -S . \
        -A x64 \
        -DCMAKE_BUILD_TYPE=Release \
        -DPA_BUILD_SHARED=ON \
        -DPA_BUILD_STATIC=OFF \
        -DPA_USE_ASIO=OFF \
        -DPA_USE_DS=OFF \
        -DPA_USE_WMME=OFF \
        -DPA_USE_WASAPI=ON \
        -DPA_USE_WDMKS=OFF \
        -DCMAKE_INSTALL_PREFIX=install
fi

# Build
echo "Building..."
cmake --build build --config Release

# Install
echo "Installing..."
cmake --install build --config Release

# Copy to runtimes directory
OUTPUT_DIR="$INSTALL_DIR/$RID/native"
mkdir -p "$OUTPUT_DIR"

if [ "$PLATFORM" == "windows" ]; then
    cp install/bin/$LIB_NAME "$OUTPUT_DIR/"
elif [ "$PLATFORM" == "macos" ]; then
    cp install/lib/$LIB_NAME "$OUTPUT_DIR/"
elif [ "$PLATFORM" == "linux" ]; then
    cp install/lib/$LIB_NAME* "$OUTPUT_DIR/"
    cd "$OUTPUT_DIR"
    # Create symlink if needed
    if [ ! -f "$LIB_NAME" ]; then
        VERSIONED_LIB=$(ls ${LIB_NAME}.* 2>/dev/null | head -1)
        if [ -n "$VERSIONED_LIB" ]; then
            ln -sf "$(basename "$VERSIONED_LIB")" "$LIB_NAME"
        fi
    fi
fi

echo ""
echo "✓ PortAudio built successfully!"
echo "✓ Library installed to: $OUTPUT_DIR"
echo ""
echo "You can now build and debug Spice86 with audio support."
