# PortAudio Native Libraries Integration

## Overview
This document describes how PortAudio native libraries are managed and integrated into the Spice86 project.

## Architecture

### Library Organization
Native PortAudio libraries are stored in `src/Bufdio.Spice86/runtimes/` using the .NET Runtime Identifier (RID) structure:

```
runtimes/
├── win-x64/native/libportaudio.dll
├── win-x86/native/libportaudio.dll
├── win-arm64/native/libportaudio.dll
├── osx-x64/native/libportaudio.2.dylib
├── osx-arm64/native/libportaudio.2.dylib
├── linux-x64/native/libportaudio.so.2
└── linux-arm64/native/libportaudio.so.2
```

### NuGet Package Integration
The `Bufdio.Spice86.csproj` file includes these libraries using conditional Content items:

```xml
<Content Include="runtimes\{RID}\native\{library}" 
         PackagePath="runtimes\{RID}\native\" 
         Pack="true" 
         CopyToOutputDirectory="PreserveNewest" 
         Condition="Exists(...)" />
```

When the NuGet package is consumed, .NET automatically:
1. Extracts the appropriate library for the target platform
2. Places it in the output directory
3. Makes it available for P/Invoke loading

## Building PortAudio Libraries

### Automated Build (Recommended)
Use the GitHub Actions workflow to build all platform libraries:

```bash
# Trigger via GitHub UI or gh CLI
gh workflow run build-portaudio.yml
```

This workflow:
- Builds PortAudio for all target platforms
- Creates artifacts for each platform
- Outputs properly structured runtime directories

After the workflow completes:
1. Download the artifacts
2. Extract them to `src/Bufdio.Spice86/runtimes/`
3. Commit the new/updated libraries
4. Create a PR with the changes

### Manual Build
For local development or specific platforms:

#### Windows
```powershell
# Requires Visual Studio or Build Tools for Visual Studio
git clone --depth 1 --branch v19.7.0 https://github.com/PortAudio/portaudio.git
cd portaudio

# For x64
cmake -B build -A x64 -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_WASAPI=ON -DPA_USE_ASIO=OFF -DPA_USE_DS=OFF -DPA_USE_WMME=OFF
cmake --build build --config Release

# For x86
cmake -B build32 -A Win32 -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_WASAPI=ON -DPA_USE_ASIO=OFF -DPA_USE_DS=OFF -DPA_USE_WMME=OFF
cmake --build build32 --config Release

# For ARM64
cmake -B buildarm64 -A ARM64 -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_WASAPI=ON -DPA_USE_ASIO=OFF -DPA_USE_DS=OFF -DPA_USE_WMME=OFF
cmake --build buildarm64 --config Release
```

#### macOS
```bash
git clone --depth 1 --branch v19.7.0 https://github.com/PortAudio/portaudio.git
cd portaudio

# For x64 (Intel)
cmake -B build -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DCMAKE_OSX_ARCHITECTURES=x86_64
cmake --build build --config Release

# For arm64 (Apple Silicon)
cmake -B buildarm -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DCMAKE_OSX_ARCHITECTURES=arm64
cmake --build buildarm --config Release
```

#### Linux
```bash
sudo apt-get install -y libasound2-dev cmake
git clone --depth 1 --branch v19.7.0 https://github.com/PortAudio/portaudio.git
cd portaudio

# For x64
cmake -B build -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_ALSA=ON -DPA_USE_JACK=OFF
cmake --build build --config Release

# For arm64 (requires cross-compilation toolchain)
sudo apt-get install -y gcc-aarch64-linux-gnu g++-aarch64-linux-gnu
cmake -B buildarm64 -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_ALSA=ON -DPA_USE_JACK=OFF -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++
cmake --build buildarm64 --config Release
```

### Build Configuration

PortAudio is configured with minimal audio backend support:
- **Windows**: WASAPI only (modern Windows audio API)
- **macOS**: CoreAudio (native macOS audio)
- **Linux**: ALSA only (standard Linux audio)

This minimizes binary size and external dependencies while providing reliable cross-platform audio.

## CI Integration

### Current Workflow
The main CI workflows (`nuget.yml`, `prerelease.yml`, `pr.yml`) use existing native libraries from the repository:

1. Libraries are committed to the repository
2. CI builds include them automatically via `Bufdio.Spice86.csproj`
3. NuGet packages contain all available platform libraries
4. Missing libraries result in a warning but don't fail the build

### Verification
Before packaging, run the verification script:

```bash
cd src/Bufdio.Spice86
./check-portaudio-libs.sh
```

This reports which platform libraries are present and which are missing.

## Updating PortAudio

When updating to a new PortAudio version:

1. Update `PORTAUDIO_VERSION` in `.github/workflows/build-portaudio.yml`
2. Run the build workflow
3. Download artifacts
4. Replace libraries in `src/Bufdio.Spice86/runtimes/`
5. Test on target platforms
6. Update this document with any changes
7. Create a PR

## Troubleshooting

### Library Not Found at Runtime
If a consuming application fails to load PortAudio:

1. **Verify the library is in the NuGet package**:
   ```bash
   unzip -l Bufdio.Spice86.*.nupkg | grep runtimes
   ```

2. **Check output directory**:
   The library should be in `bin/{Configuration}/net10.0/runtimes/{RID}/native/`

3. **Verify RID matching**:
   Ensure the application's target platform matches an available RID

### Build Failures
If PortAudio fails to build:

1. **Check dependencies**:
   - Windows: Visual Studio or Build Tools
   - macOS: Xcode Command Line Tools
   - Linux: ALSA development packages

2. **Verify CMake version**:
   Requires CMake 3.12 or later

3. **Check architecture support**:
   Some platforms may require specific toolchains (e.g., ARM64 cross-compilation on Linux)

## Design Decisions

### Why Commit Binaries?
- **Reliability**: Guarantees consistent builds
- **Speed**: CI doesn't need to compile PortAudio every time
- **Simplicity**: No complex build matrix or cross-compilation in main CI
- **Transparency**: Exact binaries shipped are in version control

### Why Separate Build Workflow?
- **On-demand**: Libraries only need rebuilding when PortAudio updates
- **Platform-specific**: Can use native runners for each platform
- **Maintainability**: Separates concerns between library building and project building

### Why Minimal Audio Backends?
- **Size**: Reduces DLL/SO size significantly
- **Dependencies**: Fewer external library dependencies
- **Compatibility**: Focuses on modern, well-supported APIs
- **Maintenance**: Fewer potential issues across platforms

## References
- [PortAudio Documentation](http://www.portaudio.com/docs/v19-doxydocs/)
- [.NET Runtime Identifiers](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)
- [NuGet Native Packages](https://learn.microsoft.com/en-us/nuget/create-packages/native-packages)
