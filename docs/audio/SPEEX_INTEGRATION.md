# Speex Resampler Integration Guide

## Status: Infrastructure Complete, Integration Pending

The Speex resampler P/Invoke bindings and wrapper classes are now implemented and ready for use. However, the actual integration into the audio sample processing pipeline is not yet complete.

## What's Implemented

### 1. Speex P/Invoke Bindings
**Location:** `src/Bufdio.Spice86/Bindings/Speex/`

- **NativeMethods.cs**: Platform-specific P/Invoke declarations for Windows, Linux, and macOS
- **SpeexError.cs**: Error code enum matching Speex resampler error codes
- **SpeexResamplerQuality.cs**: Quality settings (Fastest, Fast, Medium, High, Best)

### 2. High-Level Speex Wrapper
**Location:** `src/Bufdio.Spice86/SpeexResampler.cs`

C# wrapper providing:
- Safe initialization and disposal
- Rate conversion (SetRate)
- Float sample processing (ProcessFloat)
- Buffer management (SkipZeros, Reset)

### 3. MixerChannel Integration Points
**Location:** `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs`

- Speex resampler field declarations (lines 45-48)
- ConfigureResampler() method updated to initialize Speex for downsampling (lines 243-326)
- InitSpeexResampler() method for creating and configuring Speex instances

## What's NOT Yet Implemented

### Buffer-Level Resampling in Mix() Method

The current implementation processes samples one-by-one through AddSamples_* methods. Speex resampler works on **buffers**, not individual samples, requiring a different integration approach.

**Required Changes:**

1. **Modify Mix() Method** (MixerChannel.cs)
   - After collecting samples in AudioFrames buffer
   - Before returning frames to mixer
   - Apply Speex resampling on the entire buffer

2. **Add SpeexResampleBuffer() Method**
   ```csharp
   private void SpeexResampleBuffer(int requiredFrames) {
       if (!_speexResampler?.IsInitialized ?? true) return;
       
       // Extract L/R channels from AudioFrames
       // Call _speexResampler.ProcessFloat() for each channel
       // Rebuild AudioFrames with resampled data
   }
   ```

3. **Update Mix() Call Chain**
   - Currently: AddSamples → ConvertAndAdd → AudioFrames
   - Future: AddSamples → AudioFrames → SpeexResample → Mixer

## Native Library Requirements

### Building Speex Libraries

Speex resampling is provided by **libspeexdsp**, not libspeex. You need to build/obtain:

- **Windows**: `libspeexdsp.dll`
- **Linux**: `libspeexdsp.so.1`
- **macOS**: `libspeexdsp.1.dylib`

### Option 1: Use System Libraries (Linux/macOS)

```bash
# Debian/Ubuntu
sudo apt-get install libspeexdsp1

# macOS (Homebrew)
brew install speexdsp
```

### Option 2: Build from Source

```bash
git clone https://gitlab.xiph.org/xiph/speexdsp.git
cd speexdsp
./autogen.sh
./configure
make
sudo make install
```

### Option 3: Include in Spice86 Distribution (Recommended)

For a complete distribution, pre-built binaries should be included:

1. Build for all platforms (Windows, Linux x64, macOS arm64/x64)
2. Place in appropriate directories:
   - Windows: `src/Spice86/libspeexdsp.dll`
   - Linux: Package for distribution-specific paths
   - macOS: Bundle in .app or require Homebrew

3. Update `.csproj` to copy native libraries:
   ```xml
   <ItemGroup>
     <None Include="libspeexdsp.dll" Condition="'$(OS)' == 'Windows_NT'">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
     </None>
   </ItemGroup>
   ```

## Testing Integration

Once buffer-level resampling is implemented:

1. **Unit Tests** (tests/Spice86.Tests/)
   - Test Speex initialization
   - Verify rate conversion accuracy
   - Test buffer boundary conditions

2. **Integration Tests**
   - Run DOS programs with various sample rates
   - Compare audio output with/without Speex
   - Verify no clicks, pops, or artifacts

3. **Performance Testing**
   - Measure CPU impact of different quality settings
   - Compare Speex vs linear interpolation performance

## Error Handling

Current implementation gracefully degrades:
- If Speex library is not found, logs warning
- Falls back to existing linear interpolation or ZoH upsampling
- System continues to function without Speex

## DOSBox Staging Reference

DOSBox Staging uses Speex resampler for high-quality rate conversion:
- Quality setting: Typically "Medium" (level 5) for balanced performance
- Used when channel rate != mixer rate
- Applied after sample collection, before mixing

Reference: `dosbox-staging/src/audio/mixer.cpp`

## Next Steps

1. ✅ Create P/Invoke bindings (DONE)
2. ✅ Create C# wrapper class (DONE)
3. ✅ Add to MixerChannel fields (DONE)
4. ✅ Initialize in ConfigureResampler (DONE)
5. ⏸️ Implement buffer-level resampling in Mix() (PENDING)
6. ⏸️ Build/package native libraries for distribution (PENDING)
7. ⏸️ Add unit tests (PENDING)
8. ⏸️ Performance testing (PENDING)

## References

- Speex DSP documentation: https://speex.org/docs/
- speex_resampler.h API: https://github.com/xiph/speexdsp/blob/master/include/speex/speex_resampler.h
- DOSBox Staging mixer: https://github.com/dosbox-staging/dosbox-staging/blob/main/src/audio/mixer.cpp
