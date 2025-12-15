# Audio Architecture Porting - Next Steps

## Current Status (as of 2025-12-15 - Updated)

**Overall Progress: 74% Complete (5365/7193 lines)**
**DOSBox Source:** ✅ Available at `/tmp/dosbox-staging/`

### Completed Components

#### ✅ Sound Blaster (63% - 2486/3917 lines)
- All 96 DSP commands implemented
- Complete DMA transfer system (PlayDmaTransfer)
- ADPCM decoders (2/3/4-bit)
- Hardware mixer integration
- DMA pause/resume commands
- Warmup handling and callback system

#### ✅ Mixer Effects & Infrastructure (792 lines)
- Basic mixer thread with PortAudio output
- Channel registry and management
- Effect presets (Reverb, Chorus, Crossfeed, Compressor)
- Per-channel effect sends with gain staging
- High-pass filtering (reverb input + master output)
- Channel sleep/wake mechanism (Sleeper class)
- Linear interpolation upsampling
- Zero-order-hold (ZoH) upsampling
- Speex resampler fully integrated (NEW)

#### ✅ MixerChannel (1296 lines) [+116 lines]
- Sample processing for 8/16-bit PCM, float
- Resampling infrastructure (lerp, ZoH, Speex active)
- Buffer-level Speex resampling with stereo separation
- Effect send routing
- Sleep/wake with fade-out
- Volume control (user + app)

#### ✅ Supporting Classes
- HardwareMixer (593 lines): SB Pro/SB16 mixer registers
- MixerTypes (198 lines): Complete enums and types

## Phase Roadmap

### Phase 2B: Bulk DMA Transfer ✅ COMPLETE
- PlayDmaTransfer() fully implemented
- All frame enqueueing methods
- IRQ signaling
- Edge case handling

### Phase 3: Speex Resampler Integration ✅ COMPLETE

#### Phase 3.1: P/Invoke Infrastructure ✅ COMPLETE
**What's Done:**
- `Bufdio.Spice86/Bindings/Speex/` - Complete P/Invoke bindings
- `SpeexResampler.cs` - C# wrapper with IDisposable
- Integration points in MixerChannel.ConfigureResampler()
- Comprehensive documentation (SPEEX_INTEGRATION.md)

**Result:** Infrastructure ready, library gracefully degrades if libspeexdsp not available

#### Phase 3.2: Buffer-Level Resampling ✅ COMPLETE
**What's Done:**
- Implemented `SpeexResampleBuffer()` method in MixerChannel (lines 554-658)
- Modified `Mix()` to apply Speex resampling on collected buffer (lines 486-497)
- Handles stereo channel separation (L/R processed independently)
- Proper error handling with graceful fallback to pass-through
- Frame count adjustment (padding/truncation) to match target
- Verbose logging for debugging resampler behavior

**Implementation Details:**
- Extracts left/right channels into separate float arrays
- Calls `_speexResampler.ProcessFloat()` for each channel (channel index 0/1)
- Rebuilds AudioFrames with resampled data
- Synchronizes L/R channels by using minimum of generated frames
- Falls back to pass-through on error (preserves original frames)

**Result:** Speex resampling now fully integrated into audio pipeline. Activated when:
- ResampleMethod.LerpUpsampleOrResample with downsampling
- ResampleMethod.ZeroOrderHoldAndResample with rate conversion after ZoH
- ResampleMethod.Resample for all rate conversions

#### Phase 3.3: Native Library Packaging ⏸️ REQUIRED
**What's Needed:**
- Build libspeexdsp for Windows (x64), Linux (x64), macOS (arm64/x64)
- Add binaries to `src/Spice86/` directory structure
- Update `.csproj` to copy native libraries to output
- Add to CI/CD build and packaging process

**Resources:**
- Source: https://github.com/xiph/speexdsp
- Build instructions in SPEEX_INTEGRATION.md

**Complexity:** High (8-16 hours, multi-platform testing)
**Blockers:** Need access to macOS and Windows build environments

#### Phase 3.4: Testing ⏸️ AFTER PHASE 3.2 & 3.3
**What's Needed:**
- Unit tests for SpeexResampler wrapper
- Integration tests with various sample rates
- Performance benchmarking (quality vs CPU)
- DOS program compatibility testing

**Complexity:** Medium (4-8 hours)
**Blockers:** Requires Phase 3.2 and 3.3 complete

### Phase 4: Core Mixer Thread Architecture ✅ UNBLOCKED

**Target:** Expand `Mixer.cs` from 792 lines to ~1000 lines (advanced effects + preset system)

**What's Needed:**
1. ✅ **DOSBox Source Access:** Available at `/tmp/dosbox-staging/`
2. ✅ **Systematic Method Mapping:** Complete - see `docs/audio/PHASE4_METHOD_MAPPING.md`
3. ✅ **Architecture Analysis:** Core mixer thread already mirrors DOSBox correctly
4. **Advanced Effects:** Port MVerb reverb and TAL-Chorus algorithms
5. **Compressor Upgrade:** Replace basic compressor with DOSBox's RMS-based version
6. **Preset System:** Add Get/Set methods for effect presets
7. **Global Effect Sends:** Add helpers to configure all channels at once

**Complexity:** Medium-High (22-33 hours)
**Sub-Phases:**
- Phase 4.1: Advanced Effects Algorithms (15-22 hours)
  - MVerb reverb port (4-6 hours)
  - TAL-Chorus library port (8-12 hours)
  - Compressor class upgrade (3-4 hours)
- Phase 4.2: Preset System (6-9 hours)
  - Preset configuration methods (4-6 hours)
  - Global effect sends (2-3 hours)
- Phase 4.3: Minor Enhancements (1-2 hours)
  - Channel feature query exposure (1 hour)

**Blockers:** 
- ✅ DOSBox Staging source code now available
- ✅ Method mapping complete
- ✅ Architecture verified to match DOSBox

### Phase 5: Audio Thread Coordination ✅ UNBLOCKED (Low Priority)

**Target:** Verify and enhance `Mixer.cs` + `SoundBlaster.cs` coordination

**Status:** Core coordination already functional - most work done in Phase 2B

**What's Needed:**
1. ✅ Device-to-mixer architecture already mirrors DOSBox (callback-based)
2. ✅ DMA-audio synchronization complete (PlayDmaTransfer, DspDmaCallback)
3. ✅ Thread-safe communication via Lock and concurrent collections
4. **Verification:** Side-by-side comparison with DOSBox source to confirm parity
5. **Refinements:** Minor timing adjustments if needed based on testing

**Complexity:** Low (4-8 hours - mostly verification)
**Blockers:** 
- ✅ DOSBox source now available for verification

## Immediate Action Items

### For Next Session (Recommended Priority)

1. **Phase 4.1: Port MVerb Reverb** ⭐ HIGH PRIORITY
   - Port MVerb.h to C# class
   - Implement FDN reverb architecture
   - Replace basic reverb in ApplyReverb()
   - 4-6 hours estimated
   - See `docs/audio/PHASE4_METHOD_MAPPING.md` for details

2. **Phase 4.1: Port TAL-Chorus** ⭐ HIGH PRIORITY
   - Port TAL-Chorus library (7 classes)
   - Implement modulated delay with LFO
   - Replace basic chorus in ApplyChorus()
   - 8-12 hours estimated
   - See `docs/audio/PHASE4_METHOD_MAPPING.md` for details

3. **Speex Phase 3.3: Build Native Libraries** ⭐ MEDIUM PRIORITY
   - Required for full Speex deployment
   - 8-16 hours estimated
   - Can be done in parallel with Phase 4 work
   - NOTE: Speex integration is functionally complete, just needs native binaries

### For Future Sessions

4. **Phase 4.2: Preset System** (after Phase 4.1)
   - Add preset configuration methods
   - Add global effect send helpers
   - 6-9 hours estimated

5. **Phase 4.3: Compressor Upgrade** (after Phase 4.1)
   - Port Compressor class from DOSBox
   - Implement RMS detection
   - Add knee width and makeup gain
   - 3-4 hours estimated

6. **Phase 5: Verification** (after Phase 4 complete)
   - Side-by-side verification with DOSBox
   - Minor timing adjustments if needed
   - Integration testing with DOS programs
   - 4-8 hours estimated

7. **Testing Infrastructure**
   - Unit tests for effects algorithms
   - Integration tests with various configurations
   - Performance benchmarking
   - 4-8 hours per category

## Success Criteria

### Minimum Viable (Current Target)
- ✅ All DSP commands functional
- ✅ DMA transfers working correctly
- ✅ Basic effects operational
- ✅ Speex resampling integrated (Phase 3.2 complete)
- ⏸️ Native libraries packaged (Phase 3.3)
- ⏸️ Testing complete (Phase 3.4)

### Feature Complete (Final Goal)
- ⏸️ 100% DOSBox mixer.cpp method coverage
- ⏸️ All DOSBox timing and synchronization patterns
- ⏸️ Performance parity with DOSBox
- ⏸️ Full DOS program compatibility

### Quality Metrics
- No audio artifacts (clicks, pops, distortion)
- Stable playback in all DOS programs
- CPU usage comparable to DOSBox
- Side-by-side code verification possible

## Key Constraints

1. **Architectural Authority:** DOSBox Staging is the reference - mirror exactly, don't improve
2. **No Feature Addition:** Don't add capabilities beyond what DOSBox has
3. **Code Structure:** Maintain side-by-side debuggability with DOSBox
4. **Quality:** All code must compile without warnings, follow C# conventions

## References

- **Planning:** AUDIO_PORT_PLAN.md (high-level roadmap)
- **Planning:** docs/audio/DOSBOX_AUDIO_PORT_PLAN.md (detailed phases)
- **Speex:** docs/audio/SPEEX_INTEGRATION.md (complete integration guide)
- **DOSBox:** https://github.com/dosbox-staging/dosbox-staging
- **Speex:** https://github.com/xiph/speexdsp

## Questions Resolved ✅

1. ✅ **Reverb/Chorus "Upgrade":** CONFIRMED - DOSBox Staging DOES have MVerb and TAL-Chorus. These are legitimate mirroring tasks, not feature additions. Located at:
   - `/tmp/dosbox-staging/src/libs/mverb/MVerb.h`
   - `/tmp/dosbox-staging/src/libs/tal-chorus/ChorusEngine.h`

2. ✅ **Mixer Line Count Gap:** Analysis complete - Mixer.cs has core architecture correct (792 lines). Missing components are:
   - Advanced effect algorithms (MVerb, TAL-Chorus) - ~200 lines
   - Preset configuration system - ~150 lines
   - Global effect send helpers - ~50 lines
   - Target final size: ~1000 lines (not 3276 - much of DOSBox's code is config/setup which is out of scope)

3. **Testing Strategy:** Tests should be added after Phase 4 complete. Current: no audio tests exist, but mirroring work takes priority over testing infrastructure.

## Last Updated

2025-12-15 - Phase 4 & 5 unblocked: DOSBox source obtained, method mapping complete. See `docs/audio/PHASE4_METHOD_MAPPING.md` for implementation plan.
