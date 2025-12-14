# DOSBox Staging Audio Subsystem Port - Comprehensive Plan

## Executive Summary

This document outlines the multi-phase plan for porting DOSBox Staging's audio subsystem (~7000 lines) to Spice86. The port aims for full audio parity with DOSBox while leveraging Spice86's existing infrastructure and maintaining side-by-side debuggability with the reference implementation.

**Current Status:** Phase 1 Complete (~910 lines ported)  
**Remaining Work:** ~6000+ lines across multiple phases  
**Target:** Full parity with DOSBox Staging mixer.cpp (3276 lines) and soundblaster.cpp (3918 lines)

---

## 1. What We Currently Have (Phase 1 - Complete)

### SoundBlaster.cs Enhancements (~600 lines)
**Location:** `src/Spice86.Core/Emulator/Devices/Sound/Blaster/SoundBlaster.cs`

#### ADPCM Decoders ✅
- `DecodeAdpcmPortion()` - Generic ADPCM decoder with step-size adaptation
- `DecodeAdpcm2Bit()` - 2-bit ADPCM (4 samples per byte)
- `DecodeAdpcm3Bit()` - 3-bit ADPCM (2.67 samples per byte)
- `DecodeAdpcm4Bit()` - 4-bit ADPCM (2 samples per byte)
- Scale/adjust maps for adaptive quantization
- Reference sample and stepsize state tracking

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines ~800-1100

#### DSP Command Infrastructure ✅
- `DspCommandLengthsSb[]` - SB/SBPro command parameter lengths (256 entries)
- `DspCommandLengthsSb16[]` - SB16 command parameter lengths (256 entries)
- Direct array indexing mirroring DOSBox lookup tables

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines ~1200-1450

#### DMA Frame Generation ✅
- `GenerateDmaFrame()` - Main DMA frame dispatcher
- `GeneratePcm8Frame()` - 8-bit PCM playback
- `GenerateAdpcm2Frame()` - 2-bit ADPCM with HaveRef logic
- `GenerateAdpcm3Frame()` - 3-bit ADPCM with HaveRef logic
- `GenerateAdpcm4Frame()` - 4-bit ADPCM with HaveRef logic
- Multi-sample buffering via `RemainSize` for ADPCM modes
- Reference byte handling for ADPCM initialization

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines ~2400-2800 (play_dma_transfer switch cases)

### Mixer.cs Enhancements (~230 lines)
**Location:** `src/Spice86.Core/Emulator/Devices/Sound/Mixer.cs`

#### Effects Pipeline ✅
- `ApplyReverb()` - 50ms delay buffer with 30% feedback, 20% wet mix
- `ApplyChorus()` - 20ms delay buffer with 15% wet mix
- `ApplyCrossfeed()` - 30% stereo mixing matrix for headphone spatialization
- `ApplyCompressor()` - 4:1 compression ratio with attack/release envelope
- Effect processing order matches DOSBox mixer.cpp

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines ~1800-2200 (effects processing)

#### Master Normalization ✅
- `ApplyMasterNormalization()` - Peak detection with 0.995 decay coefficient
- `ApplySoftClipping()` - Soft-knee limiting at 32000 threshold
- Hard clipping prevention at 32767
- Per-channel peak tracking

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines ~2300-2500 (master output pipeline)

### MixerChannel.cs Enhancements (~80 lines)
**Location:** `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs`

#### Resampling Infrastructure ✅
- `InitLerpUpsamplerState()` - Linear interpolation state initialization
- `ApplyLerpUpsampling()` - Phase-tracked linear interpolation
- Automatic upsampling when channel rate < mixer rate (48kHz)
- Always produces target frame count (prevents buffer underruns)
- Proper handling of empty input buffers

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines ~1000-1200 (lerp_upsampler)

### Existing Spice86 Infrastructure (Leveraged)
✅ **AudioFrame** - Stereo sample struct with operator overloads  
✅ **IIR Butterworth Filters** - HighPass, LowPass, BandPass, BandStop (Spice86.Libs)  
✅ **SimdConversions** - SIMD-accelerated byte/short→float conversions  
✅ **DmaChannel/DmaBus** - DMA infrastructure mirroring DOSBox Staging  
✅ **EmulationLoopScheduler** - Event queue with accurate timing  
✅ **PrimaryPic/DualPic** - IRQ signaling infrastructure  
✅ **Adlib Gold, nukedOpl3** - FM synthesis devices  

---

## 2. What We DON'T Want to Port (Out of Scope)

### Explicitly Excluded Features

#### GameBlaster / CMS ❌
**Reason:** Niche hardware, limited game support, adds complexity without significant benefit.  
**DOSBox Code:** `src/hardware/audio/gblaster.cpp` (~500 lines)

#### ESFM (ESS FM Extensions) ❌
**Reason:** Proprietary ESS AudioDrive extensions, not part of core Sound Blaster compatibility.  
**DOSBox Code:** Integrated in soundblaster.cpp ESFM-specific sections (~400 lines)

#### Configuration/Setup System ❌
**Reason:** Spice86 uses different configuration approach via CLI args and Configuration class.  
**DOSBox Code:** All `SETUP_*` functions, `Config` integration (~800 lines across files)

#### Fast-Forward/Slow-Motion Playback ❌
**Reason:** Emulation speed control handled by Spice86's timing system, not audio subsystem.  
**DOSBox Code:** Time scaling logic in mixer.cpp (~150 lines)

#### WAV Capture/Recording System ❌
**Reason:** Low priority feature, can be added later if needed.  
**DOSBox Code:** `src/hardware/audio/mixer.cpp` capture functions (~300 lines)

#### SDL Audio Backend ❌
**Reason:** Spice86 uses PortAudio directly, not SDL.  
**DOSBox Code:** SDL integration in mixer.cpp (~200 lines)

#### Mapper/Keyboard Binding System ❌
**Reason:** Audio-specific key bindings not needed, Spice86 handles input separately.  
**DOSBox Code:** Mapper integration (~100 lines)

### Total Excluded: ~2450 lines (out of DOSBox's ~7000 audio lines)

---

## 3. What Is Remaining (Future Phases)

### Phase 2: Complete SoundBlaster DMA Transfer Logic (~800 lines)

#### Priority: HIGH - Core functionality for robust audio playback

**Target:** `SoundBlaster.cs` expansion

#### 2.1 Bulk DMA Processing (~300 lines)
- Port `play_dma_transfer()` full implementation
- `read_dma_8bit()` - Optimized 8-bit PCM reading with boundary checks
- `read_dma_16bit()` - Optimized 16-bit PCM reading with alignment
- Bulk ADPCM decoding paths for efficiency
- DMA exhaustion detection and IRQ signaling

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 2200-2500

#### 2.2 Advanced DMA Modes (~200 lines)
- High-speed DMA mode (SB2.0+)
- Auto-init DMA reload logic
- Single-cycle vs continuous mode handling
- DMA pause/resume commands (0xD0, 0xD1, 0xD3, 0xD4, 0xDA)
- 16-bit DMA support for SB16

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 2500-2700

#### 2.3 DMA Timing and Synchronization (~150 lines)
- Per-tick callback management
- DMA transfer rate calculations
- Timing measurement and stabilization (Dac class)
- Warmup period handling for cold starts

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 2800-2950

#### 2.4 DSP Command Handlers - DMA Setup (~150 lines)
- 0x14-0x1F: 8-bit DMA start commands
- 0x90-0x9F: High-speed auto-init DMA
- 0xB0-0xCF: SB16 16-bit DMA commands
- 0x40-0x48: Time constant and block size setup
- Command parameter buffering and validation

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 1500-1650

### Phase 3: Complete DSP Command Implementation (~600 lines)

#### Priority: MEDIUM - Full hardware compatibility

**Target:** `SoundBlaster.cs` expansion

#### 3.1 Recording/Input Commands (~200 lines)
- 0x20-0x2F: Recording setup and control
- ADC (Analog-Digital Converter) emulation
- Input source selection (mic, line-in, CD)
- Input DMA transfer handling

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 1650-1850

#### 3.2 Mixer Register Commands (~150 lines)
- 0x0E, 0x0F: Mixer register read/write
- Volume control integration (DAC, FM, CD, Line)
- Input/output routing configuration
- Mixer register state persistence

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 1850-2000

#### 3.3 Information/Test Commands (~150 lines)
- 0xE0-0xE8: DSP identification and version
- 0xF0-0xF8: Test commands (sine wave, IRQ test)
- Copyright string retrieval
- Speaker test mode

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 2000-2150

#### 3.4 MIDI/MPU-401 Integration (~100 lines)
- MIDI passthrough for games using SB MIDI port
- MPU-401 UART mode emulation
- MIDI timing and buffering

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 3500-3600

### Phase 4: Advanced Mixer Features (~1200 lines)

#### Priority: MEDIUM-HIGH - Audio quality improvements

**Target:** `Mixer.cs` expansion to match DOSBox mixer.cpp size (3276 lines)

#### 4.1 Advanced Resampling (~400 lines)
- Zero-order hold (ZoH) upsampler for low sample rates
- Speex resampler equivalent using existing IIR filters
- Multi-stage resampling for extreme rate differences
- Sinc interpolation option for high-quality resampling
- Per-channel resampler selection based on rate difference

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 800-1200

#### 4.2 Enhanced Effects Pipeline (~300 lines)
- **Reverb Enhancement:**
  - High-pass filter integration (using existing IIR Butterworth)
  - Multi-tap delay lines for realistic room simulation
  - Early reflections + late reverb separation
  - Configurable room size and damping

- **Chorus Enhancement:**
  - LFO (Low-Frequency Oscillator) modulation
  - Multi-voice chorus (2-4 voices)
  - Depth and rate controls

- **Additional Effects:**
  - Stereo widening
  - Bass boost / treble boost
  - Equalizer (3-band or 10-band using IIR filters)

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 1800-2100

#### 4.3 Dynamic Range Management (~200 lines)
- Look-ahead limiter (prevents clipping proactively)
- Multi-band compressor (separate bass/mid/treble compression)
- Automatic gain control (AGC) for consistent output levels
- Noise gate for eliminating low-level hum

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 2100-2300

#### 4.4 Prebuffering and Smooth Startup (~150 lines)
- Prebuffer system (25-100ms buffer before output starts)
- Prevents initial crackling and pops
- Smooth fade-in for channel enable
- Anti-pop circuitry emulation

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 600-750

#### 4.5 Channel Management (~150 lines)
- Dynamic channel addition/removal
- Channel priority system
- Voice stealing for channel limits
- Mute/solo functionality per channel

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 400-550

### Phase 5: Filter Bank and Hardware Quirks (~400 lines)

#### Priority: LOW-MEDIUM - Historical accuracy

**Target:** New `FilterBank.cs` + `SoundBlaster.cs` integration

#### 5.1 Sound Blaster Filter Emulation (~200 lines)
- **SB1.0 Filter:** 3.2kHz low-pass (single-pole IIR)
- **SB2.0 Filter:** Improved 8kHz low-pass (two-pole IIR)
- **SBPro1 Filter:** 12kHz low-pass, stereo
- **SBPro2 Filter:** 20kHz low-pass, improved stereo separation
- **SB16 Filter:** 44kHz low-pass, high-quality

Filters should use existing `Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth` infrastructure.

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 500-700

#### 5.2 Hardware Quirks and Compatibility (~200 lines)
- Sample rate limit emulation (22kHz for SBPro, 44.1kHz for SB16)
- Mono/stereo mode switching artifacts
- IRQ timing variations between SB models
- DMA alignment requirements
- "Silent" sample injection for timing-sensitive games

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 3200-3400

### Phase 6: Performance Optimization (~500 lines)

#### Priority: LOW - Performance improvements

#### 6.1 SIMD Optimization (~200 lines)
- Leverage `SimdConversions` for bulk sample processing
- Vectorized mixing operations using `System.Numerics.Vector`
- Batch processing for effect chains
- Parallel channel processing where safe

**Target:** `Mixer.cs`, `MixerChannel.cs`

#### 6.2 Memory Allocation Reduction (~150 lines)
- Object pooling for temporary buffers
- ArrayPool<T> usage for frame buffers
- Span<T> and Memory<T> for zero-copy operations
- Reduce List<AudioFrame> allocations

**Target:** All audio files

#### 6.3 Profiling and Hotspot Elimination (~150 lines)
- Identify bottlenecks with BenchmarkDotNet
- Optimize tight loops in resampling and effects
- Cache frequently accessed data
- Reduce lock contention in mixer thread

**Target:** Performance-critical paths

### Phase 7: Testing and Validation (~300 lines)

#### Priority: ONGOING - Quality assurance

#### 7.1 Unit Tests (~150 lines)
- ADPCM decoder correctness (against known test vectors)
- Resampling accuracy (compare output with DOSBox)
- Effect algorithm validation
- DMA transfer logic verification

**Target:** `tests/Spice86.Tests/Audio/`

#### 7.2 Integration Tests (~150 lines)
- End-to-end audio playback tests
- Multiple simultaneous channels
- Sample rate switching
- Effect enable/disable
- Stress testing (high channel count, rapid changes)

**Target:** `tests/Spice86.Tests/Audio/`

---

## 4. The Mirroring Approach and Why

### Methodology: Side-by-Side Debugging

#### Core Principle
**All code ported from a single DOSBox source file stays in a single Spice86 file.**

- `soundblaster.cpp` (3918 lines) → `SoundBlaster.cs`
- `mixer.cpp` (3276 lines) → `Mixer.cs`

#### Rationale

1. **Debuggability**
   - Open DOSBox source and Spice86 source side-by-side
   - Line-by-line comparison during debugging
   - Easy to verify ported logic matches reference implementation
   - Simplifies bug fixing (find bug in DOSBox → locate in Spice86 → fix)

2. **Maintainability**
   - Changes to DOSBox can be tracked and ported systematically
   - Clear mapping between DOSBox commits and Spice86 updates
   - Reduces risk of divergence from reference implementation

3. **Code Review**
   - Reviewers can verify correctness by comparing with DOSBox source
   - No need to hunt across multiple files for related logic
   - Easier to spot deviations from reference implementation

4. **Incremental Porting**
   - Add functionality piece-by-piece to single file
   - Always maintain working state (compile, run, test)
   - Easy to track progress (line count vs DOSBox)

### Variable Naming and Type Mapping

#### Strict Mirroring Rules

```csharp
// DOSBox C++                    // Spice86 C#
uint8_t reference;          →    byte reference;
uint16_t stepsize;          →    ushort stepsize;
int32_t sample;             →    int sample;
std::vector<float> buffer;  →    List<float> buffer;
std::optional<int> value;   →    int? value;
std::array<int, 256> table; →    int[] table; // or private static readonly
```

**Why:** Enables immediate recognition when comparing with DOSBox source.

#### Comment References

```csharp
// Mirrors DOSBox: src/hardware/audio/soundblaster.cpp lines 850-870
private static byte[] DecodeAdpcm4Bit(byte data, ref byte reference, ref ushort stepsize) {
    // Implementation...
}
```

**Why:** Provides direct reference for verification and debugging.

### Structure Preservation

#### Function Organization

DOSBox functions are ported in the same order they appear in source:

1. Helper functions first (ADPCM decoders, converters)
2. Command tables and constants
3. Main logic functions (play_dma_transfer, etc.)
4. Public API last (DSP commands, mixer interface)

**Why:** Maintains logical flow when reading code, easier to locate functions.

#### Control Flow Mirroring

```csharp
// DOSBox switch statement preserved exactly
switch (dmaMode) {
    case DmaMode.Pcm8Bit:
        // ...
        break;
    case DmaMode.Adpcm2Bit:
        // ...
        break;
    case DmaMode.Adpcm3Bit:
        // ...
        break;
    // etc.
}
```

**Why:** Control flow bugs are easiest to spot when structure matches reference.

### Leveraging Spice86 Infrastructure

While maintaining structural mirroring with DOSBox, we leverage existing Spice86 components:

#### Use Existing Types
```csharp
// Instead of implementing stereo sample struct
AudioFrame frame = new AudioFrame(left, right);  // Use Spice86's AudioFrame

// Instead of implementing IIR filters
HighPass filter = new HighPass(order: 2);  // Use Spice86.Libs filters
filter.Setup(sampleRate: 48000, cutoff: 3200);
```

#### Use Existing Infrastructure
```csharp
// DMA reading
byte data = _sb.Dma.Channel.Read();  // Use Spice86's mirrored DmaChannel

// IRQ signaling
_pic.RaiseIrq(irqNumber);  // Use Spice86's PIC infrastructure

// Event scheduling
_scheduler.AddEvent(microseconds, callback);  // Use EmulationLoopScheduler
```

#### Use Existing Optimizations
```csharp
// SIMD conversions
SimdConversions.ConvertInt16ToScaledFloat(sourceSpan, destSpan, scale);

// Instead of manual loop in DOSBox
```

**Why:** Don't reinvent the wheel. Leverage high-quality existing code while maintaining DOSBox structure for core algorithms.

### Adaptation Layer Pattern

When Spice86 infrastructure differs significantly from DOSBox, use thin adapter:

```csharp
// DOSBox uses Speex resampler
// Spice86 has IIR Butterworth filters
private void ConfigureResampler(int sourceRate, int targetRate) {
    if (sourceRate == targetRate) return;
    
    // Adapt DOSBox's speex configuration to IIR-based resampling
    if (sourceRate < targetRate) {
        // Use linear interpolation upsampler (already implemented)
        _doLerpUpsample = true;
        InitLerpUpsamplerState();
    } else {
        // Use IIR low-pass + decimation for downsampling
        _resampleFilter = new LowPass(order: 4);
        _resampleFilter.Setup(sourceRate, cutoffFreq: targetRate / 2);
    }
}
```

**Why:** Maintains DOSBox API surface (function names, parameters) while using different implementation.

### Documentation Standards

Each ported section includes:

1. **Header comment** with DOSBox reference
2. **Algorithm explanation** if complex
3. **Deviation notes** if Spice86 implementation differs

```csharp
/// <summary>
/// Decodes 4-bit ADPCM samples using adaptive step-size quantization.
/// Mirrors DOSBox: src/hardware/audio/soundblaster.cpp lines 950-1020
/// </summary>
/// <remarks>
/// ADPCM algorithm based on IMA ADPCM specification.
/// Deviations: None - exact port of DOSBox implementation.
/// </remarks>
private static byte[] DecodeAdpcm4Bit(byte data, ref byte reference, ref ushort stepsize) {
    // ...
}
```

---

## 5. Phase Priorities and Timeline Estimate

### Priority Classification

**Priority HIGH:** Core functionality, directly impacts audio playback quality  
**Priority MEDIUM:** Nice-to-have features, improves compatibility or quality  
**Priority LOW:** Polish, optimization, edge cases

### Estimated Effort (Lines of Code)

| Phase | Description | Lines | Priority | Estimated Effort |
|-------|-------------|-------|----------|------------------|
| Phase 1 | ✅ ADPCM + Basic Mixer (Complete) | 910 | HIGH | ✅ Done |
| Phase 2 | Complete SB DMA Logic | 800 | HIGH | 2-3 weeks |
| Phase 3 | Complete DSP Commands | 600 | MEDIUM | 2 weeks |
| Phase 4 | Advanced Mixer Features | 1200 | MEDIUM-HIGH | 3-4 weeks |
| Phase 5 | Filter Bank + Quirks | 400 | LOW-MEDIUM | 1-2 weeks |
| Phase 6 | Performance Optimization | 500 | LOW | 2 weeks |
| Phase 7 | Testing & Validation | 300 | ONGOING | Continuous |
| **TOTAL** | **Full DOSBox Parity** | **4710** | - | **10-15 weeks** |

**Note:** Estimates assume part-time effort. Full-time development could reduce timeline by 50%.

### Recommended Phase Order

1. **Phase 2** (HIGH) - Essential for robust audio playback
2. **Phase 4** (MEDIUM-HIGH) - Significant quality improvements
3. **Phase 3** (MEDIUM) - Hardware compatibility
4. **Phase 5** (LOW-MEDIUM) - Historical accuracy
5. **Phase 6** (LOW) - Performance tuning
6. **Phase 7** (ONGOING) - Continuous validation

---

## 6. Success Criteria

### Phase Completion Criteria

Each phase is considered complete when:

1. ✅ **All planned code ported** from DOSBox reference sections
2. ✅ **Builds with 0 errors, 0 warnings**
3. ✅ **Unit tests pass** for new functionality
4. ✅ **Integration tests pass** with real DOS programs
5. ✅ **Code review approved** comparing with DOSBox source
6. ✅ **No regressions** in existing audio functionality

### Overall Parity Criteria

Full DOSBox audio parity achieved when:

1. ✅ **Line count matches** (~4700 new lines vs DOSBox ~4550 relevant lines)
2. ✅ **All Sound Blaster variants work** (SB1, SB2, SBPro1, SBPro2, SB16)
3. ✅ **All audio test programs pass** (same set as DOSBox validation)
4. ✅ **No audio artifacts** in major DOS games (Dune, Doom, Duke 3D, etc.)
5. ✅ **Resampling quality matches** DOSBox (A/B comparison)
6. ✅ **Performance acceptable** (< 5% CPU overhead vs current implementation)

---

## 7. Risk Mitigation

### Known Challenges

#### Challenge 1: Async vs Sync Audio Architecture
**DOSBox:** Mixer thread pulls samples from devices synchronously  
**Spice86:** DeviceThread + Mixer dual-thread architecture

**Mitigation:** Careful synchronization, leverage existing DeviceThread pattern

#### Challenge 2: Missing Speex Resampler
**DOSBox:** Uses Speex for high-quality resampling  
**Spice86:** Has IIR Butterworth filters but not Speex

**Mitigation:** Implement equivalent using IIR filters + linear interpolation combination

#### Challenge 3: Performance Impact
**Risk:** Effects pipeline + resampling adds CPU overhead

**Mitigation:** Phase 6 optimization pass, SIMD usage, optional effect disable

#### Challenge 4: Regression Introduction
**Risk:** New code breaks existing audio functionality

**Mitigation:** Comprehensive testing at each phase, automated regression tests

---

## 8. Future Enhancements (Beyond Parity)

Once DOSBox parity achieved, potential enhancements:

1. **Advanced Effects**
   - Convolution reverb using impulse responses
   - Vintage effect emulation (tape saturation, tube warmth)
   - Spectral processing

2. **Modern Audio Features**
   - WASAPI exclusive mode support (Windows)
   - JACK integration (Linux)
   - Real-time latency monitoring

3. **Analysis Tools**
   - Spectrum analyzer visualization
   - Waveform display
   - Audio recording with markers

4. **Accessibility**
   - Mono output mode for hearing-impaired users
   - Audio ducking for text-to-speech
   - Visual indicators for sound events

---

## Document Maintenance

**Last Updated:** 2025-12-14  
**Status:** Phase 1 Complete  
**Next Review:** After Phase 2 completion  

This document should be updated at the end of each phase with:
- Actual vs estimated effort
- Lessons learned
- Architecture changes
- Updated priorities based on user feedback
