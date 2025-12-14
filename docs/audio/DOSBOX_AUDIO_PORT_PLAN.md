# DOSBox Staging Audio Subsystem Port - Focused Plan

## Executive Summary

This document outlines the plan for porting DOSBox Staging's audio subsystem to Spice86 **to fix core audio rendering issues**. The ONLY priority is to mirror DOSBox Staging mixer.cpp and soundblaster.cpp architecture and management exactly.

**Objective:** Fix Spice86's core audio rendering problems by mirroring DOSBox's proven audio architecture  
**Current Status:** Phase 1 Complete (~910 lines ported)  
**Remaining Core Work:** ~3600 lines to mirror essential DOSBox audio  
**Target:** Functional parity with DOSBox Staging mixer.cpp (3276 lines) and soundblaster.cpp (3918 lines) for audio rendering

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

### Phase 4: Core Mixer Thread Architecture (~1200 lines)

#### Priority: CRITICAL - Essential for audio rendering fixes

**Target:** `Mixer.cs` expansion to mirror DOSBox mixer.cpp architecture

#### 4.1 Mixer Thread Management (~300 lines)
- Mirror DOSBox's mixer thread lifecycle and timing
- Proper audio callback synchronization
- Frame timing and buffer management
- Thread-safe channel registration/deregistration

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 400-700

#### 4.2 Advanced Resampling (~400 lines)
- Zero-order hold (ZoH) upsampler for low sample rates
- Multi-stage resampling for extreme rate differences
- Per-channel resampler selection based on rate difference
- Proper phase alignment between channels

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 800-1200

#### 4.3 Channel Mixing and Accumulation (~300 lines)
- Mirror DOSBox's per-channel mixing logic
- Proper gain staging and normalization
- Sample accumulation with overflow prevention
- Channel enable/disable handling

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 1200-1500

#### 4.4 Output Pipeline (~200 lines)
- Pre-buffering system for smooth startup
- Output frame generation matching DOSBox exactly
- Clipping prevention and limiting
- Master output formatting

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 2300-2500

### Phase 5: Audio Thread Coordination (~400 lines)

#### Priority: CRITICAL - Fixes timing and synchronization issues

**Target:** `Mixer.cs` + `SoundBlaster.cs` integration

#### 5.1 Device-to-Mixer Coordination (~200 lines)
- Mirror DOSBox's callback architecture
- Synchronous sample pulling from devices
- Proper event ordering and timing
- Lock-free communication patterns where possible

**DOSBox Reference:** `src/hardware/audio/mixer.cpp` lines 1500-1700

#### 5.2 DMA-Audio Synchronization (~200 lines)
- DMA transfer timing coordination
- IRQ signaling at correct sample boundaries
- Buffer exhaustion handling
- Auto-init DMA reload timing

**DOSBox Reference:** `src/hardware/audio/soundblaster.cpp` lines 2700-2900

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

## 5. Phase Priorities and Timeline

### Priority Classification

**ALL PHASES ARE CRITICAL:** The ONLY goal is to mirror DOSBox to fix audio rendering issues. No "nice to have" features - only exact DOSBox mirroring.

### Estimated Effort (Lines of Code)

| Phase | Description | Lines | Priority | Estimated Effort |
|-------|-------------|-------|----------|------------------|
| Phase 1 | ✅ ADPCM + Basic Mixer (Complete) | 910 | CRITICAL | ✅ Done |
| Phase 2 | Complete SB DMA Logic | 800 | CRITICAL | 2-3 weeks |
| Phase 3 | Complete DSP Commands | 600 | CRITICAL | 2 weeks |
| Phase 4 | Core Mixer Thread Architecture | 1200 | CRITICAL | 3-4 weeks |
| Phase 5 | Audio Thread Coordination | 400 | CRITICAL | 1-2 weeks |
| **TOTAL** | **Core DOSBox Mirroring** | **3910** | - | **8-11 weeks** |

**Focus:** Mirror DOSBox architecture exactly to fix Spice86's core audio rendering problems.

### Execution Order

1. **Phase 2** (CRITICAL) - DMA transfer logic fixes
2. **Phase 4** (CRITICAL) - Mixer thread architecture fixes
3. **Phase 5** (CRITICAL) - Device-mixer coordination fixes
4. **Phase 3** (CRITICAL) - Complete DSP command support

---

## 6. Success Criteria

### Core Objective

**Audio rendering works correctly in Spice86 by mirroring DOSBox architecture.**

Success achieved when:

1. ✅ **No audio artifacts** (crackling, pops, distortion)
2. ✅ **Proper timing** (DMA/IRQ/mixer synchronization)
3. ✅ **Stable playback** in DOS programs (Dune, Doom, etc.)
4. ✅ **Architecture mirrors DOSBox** (side-by-side verifiable)

---

## 7. Known Challenges

### Challenge 1: Thread Architecture Mismatch
**DOSBox:** Single mixer thread pulls samples synchronously  
**Spice86:** DeviceThread + Mixer dual-thread architecture

**Solution:** Refactor to mirror DOSBox's synchronous pull model

### Challenge 2: Timing Synchronization
**DOSBox:** Mixer callback drives device sample generation  
**Spice86:** DeviceThread pushes samples independently

**Solution:** Port DOSBox's callback-driven architecture exactly

---

## Document Maintenance

**Last Updated:** 2025-12-14  
**Status:** Phase 1 Complete - Focused plan for core audio fixes  
**Next Review:** After Phase 2 completion
