# PortAudio Native Libraries

This directory contains platform-specific PortAudio native libraries for cross-platform audio support in Bufdio.Spice86.

## Structure

The libraries are organized using .NET Runtime Identifiers (RIDs):

```
runtimes/
├── win-x64/native/libportaudio.dll       # Windows 64-bit
├── win-x86/native/libportaudio.dll       # Windows 32-bit
├── win-arm64/native/libportaudio.dll     # Windows ARM64
├── osx-x64/native/libportaudio.2.dylib   # macOS Intel
├── osx-arm64/native/libportaudio.2.dylib # macOS Apple Silicon
├── linux-x64/native/libportaudio.so.2    # Linux 64-bit
└── linux-arm64/native/libportaudio.so.2  # Linux ARM64
```

## Building PortAudio

To build PortAudio libraries for all platforms, use the GitHub Actions workflow:

```bash
# Trigger the build workflow
gh workflow run build-portaudio.yml
```

Or build manually using the PortAudio source (v19.7.0 or later):

### Windows
```cmd
cmake -B build -S . -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_WASAPI=ON -DPA_USE_ASIO=OFF -DPA_USE_DS=OFF -DPA_USE_WMME=OFF
cmake --build build --config Release
```

### macOS
```bash
cmake -B build -S . -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF
cmake --build build --config Release
```

### Linux
```bash
cmake -B build -S . -DCMAKE_BUILD_TYPE=Release -DPA_BUILD_SHARED=ON -DPA_BUILD_STATIC=OFF -DPA_USE_ALSA=ON -DPA_USE_JACK=OFF
cmake --build build --config Release
```

## Integration

These libraries are automatically included in the Bufdio.Spice86 NuGet package and will be deployed to the appropriate runtime directories when the package is consumed by .NET applications.

The .NET runtime will automatically load the correct library based on the target platform.
