# Building PortAudio for Local Development

This directory contains scripts to build PortAudio native libraries locally for debugging and development.

## Why Build Locally?

While CI automatically builds PortAudio for all platforms, developers may want to build locally to:
- Debug Spice86 with full audio support
- Test audio changes without waiting for CI
- Develop on platforms not yet supported by CI

## Quick Start

### Linux / macOS

```bash
./build-portaudio-local.sh
```

### Windows

```powershell
.\build-portaudio-local.ps1
```

For Windows x86 or ARM64:
```powershell
.\build-portaudio-local.ps1 -Architecture x86
.\build-portaudio-local.ps1 -Architecture arm64
```

## Prerequisites

### All Platforms
- CMake 3.12 or later
- Git
- C/C++ compiler toolchain

### Linux
- ALSA development libraries: `sudo apt-get install libasound2-dev`
- GCC

### macOS
- Xcode Command Line Tools: `xcode-select --install`

### Windows
- Visual Studio 2019 or later (with C++ workload)
- Or Build Tools for Visual Studio

## What the Scripts Do

1. Clone PortAudio v19.7.0 from GitHub
2. Configure CMake with minimal backends:
   - Windows: WASAPI only
   - macOS: CoreAudio only
   - Linux: ALSA only
3. Build the shared library
4. Install to `src/Bufdio.Spice86/runtimes/{RID}/native/`

## Output Locations

Libraries are installed to:
- Windows x64: `src/Bufdio.Spice86/runtimes/win-x64/native/libportaudio.dll`
- Windows x86: `src/Bufdio.Spice86/runtimes/win-x86/native/libportaudio.dll`
- Windows ARM64: `src/Bufdio.Spice86/runtimes/win-arm64/native/libportaudio.dll`
- macOS x64: `src/Bufdio.Spice86/runtimes/osx-x64/native/libportaudio.2.dylib`
- macOS ARM64: `src/Bufdio.Spice86/runtimes/osx-arm64/native/libportaudio.2.dylib`
- Linux x64: `src/Bufdio.Spice86/runtimes/linux-x64/native/libportaudio.so.2`
- Linux ARM64: `src/Bufdio.Spice86/runtimes/linux-arm64/native/libportaudio.so.2`

## After Building

Once built, the library is automatically included in your debug/release builds via the runtime identifier system. Just build and run Spice86 normally:

```bash
cd src/Spice86
dotnet run
```

## CI Integration

The CI pipeline automatically builds PortAudio for all platforms:
- On pull requests: Builds libraries and runs tests with them
- On master pushes: Builds libraries and includes in NuGet packages
- On prerelease: Builds libraries and includes in release artifacts

No manual intervention is needed for CI builds.

## Troubleshooting

### "CMake not found"
Install CMake from https://cmake.org/download/ or via your package manager.

### "ALSA not found" (Linux)
Install ALSA development libraries:
```bash
sudo apt-get update
sudo apt-get install libasound2-dev
```

### "Visual Studio not found" (Windows)
Install Visual Studio with C++ workload or Build Tools for Visual Studio.

### Build fails on macOS ARM64
Make sure you're running on Apple Silicon hardware and have Xcode Command Line Tools installed.

## Cleaning Up

To rebuild from scratch, delete the build directory:
```bash
rm -rf build/portaudio
```

Then run the build script again.
