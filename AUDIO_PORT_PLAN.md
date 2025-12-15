// SPICE86 AUDIO PARITY PORT PLAN (UPDATED)
// ==========================================
// Port DOSBox Staging audio subsystem to achieve feature parity.
// Reference: https://github.com/dosbox-staging/dosbox-staging
//
// Excludes: Fast-forward, Capture, ESFM
// Speex: Will be integrated via P/Invoke (compiled library, not translated to C#)

// PHASE 1: SoundBlaster.cpp - Complete DSP Command Set [100% COMPLETE]
// =====================================================================
// ✓ COMPLETED:
// - DAC class with rate measurement (lines 46-79)
// - ADPCM decoders (2/3/4-bit with step-size adaptation) (lines 199-303)
// - DSP command length tables (DspCommandLengthsSb/Sb16) (lines 397-437)
// - 96 unique DSP command case values (DOSBox has 96) - ALL IMPLEMENTED
// - All commands verified to exist in DOSBox soundblaster.cpp
// - SbInfo structure with complete state management
// - E2 increment table for DMA identification
// - Bulk DMA read methods (ReadDma8Bit/ReadDma16Bit)
// - Case 0x05 (SB16 ASP set codec parameter) - ADDED
// - HardwareMixer integration with full register read/write support
// - Mixer register read/write handlers (volume routing) via HardwareMixer class
//
// REMAINING:
// - Fine-tune DMA/IRQ event coordination with mixer callbacks
// - Speaker warmup timing refinements

// PHASE 2: Mixer.cpp - Core Mixing & Effects Pipeline [90% COMPLETE]
// ====================================================================
// ✓ COMPLETED:
// - Basic mixer thread loop with direct PortAudio output
// - Channel registry (ConcurrentDictionary)
// - Effect preset enums (Crossfeed, Reverb, Chorus) matching DOSBox
// - Effect configuration methods (Get/Set for each preset)
// - User and App volume controls per channel
// - Combined volume calculation (user * app * db0)
// - Linear interpolation upsampling (_doLerpUpsample)
// - Zero-order-hold (ZoH) upsampling for vintage DAC sound
// - ResampleMethod enum and configuration (LerpUpsampleOrResample, ZeroOrderHoldAndResample, Resample)
// - Basic reverb (delay + feedback, 50ms @ 48kHz)
// - Basic chorus (delay, 20ms @ 48kHz)
// - Basic crossfeed (30% stereo mix)
// - Basic compressor (4:1 ratio, -6dB threshold)
// - Peak tracking normalization
// - Per-channel effect send levels (SetReverbLevel, SetChorusLevel, SetCrossfeedStrength)
// - Global effect helpers (SetGlobalReverb, SetGlobalChorus, SetGlobalCrossfeed)
// - Effect aux buffers with proper send routing (mirrors mixer.cpp:2426-2434)
// - LockMixerThread/UnlockMixerThread for critical operations
// - AddSamples_mfloat/AddSamples_sfloat for 32-bit float samples
// - High-pass filtering on reverb input (120Hz cutoff, 2nd-order Butterworth)
// - High-pass filtering on master output (3Hz DC-blocking, 2nd-order Butterworth)
// - Channel sleep/wake mechanism (Sleeper class with fade-out and signal detection)
//   * MaybeFadeOrListen() - applies fade or detects signal per frame
//   * MaybeSleep() - checks if channel should sleep after timeout
//   * WakeUp() - called from device I/O to wake sleeping channels
//   * ConfigureFadeOut() - configurable wait/fade times
//
// REMAINING:
// - Speex resampler integration via P/Invoke (compile Speex for all platforms, ship with Spice86)
//   * Speex library is too complex to translate to C# - use native library via P/Invoke
//   * Build and package Speex binaries for Windows, Linux, macOS
//   * Integrate into Spice86 build/packaging process
// - Upgrade reverb to proper algorithm (MVerb-like or simple Schroeder)
// - Upgrade chorus to proper algorithm (TAL-Chorus-like with LFO)
// - Output prebuffering for smooth startup (NOTE: PortAudio already provides internal buffering)

// PHASE 3: Integration & Synchronization
// =======================================
// - PIC IRQ signaling through EmulationLoopScheduler
// - DMA channel coordination
// - Mixer thread timing (frame-based vs tick-based callbacks)
// - DAC rate negotiation with actual game writes

// STRATEGY
// ========
// 1. Port SoundBlaster.cpp in sections:
//    a. Constants & Enums (straightforward)
//    b. Dac class & rate measurement
//    c. ADPCM decoders
//    d. SbInfo structure (state management)
//    e. DSP command handlers (the 3000+ line bulk)
//    f. DMA event handlers
// 
// 2. Expand Mixer.cs incrementally:
//    a. Resampling per channel
//    b. Effect pipeline (reverb, chorus, crossfeed, compressor)
//    c. Master gain/compression
//    d. Output normalization
//
// 3. Test at each phase against DOSBox behavior
//
// MIRRORING CONVENTIONS
// =====================
// When mirroring DOSBox code to Spice86, maintain consistency:
//
// - Method Names: Use PascalCase (C# convention) for DOSBox method names
//   Example: ConvertSamplesAndMaybeZohUpsample() mirrors convert_samples_and_maybe_zoh_upsample()
//
// - Class Names: Use PascalCase (C# convention) for DOSBox class/struct names
//   Example: MixerChannel mirrors mixer_channel
//
// - Comments: Preserve DOSBox comments where relevant, adapting for C# context
//   Include references to DOSBox source lines for traceability
//   Example: // Mirrors DOSBox mixer.cpp:1871
//
// - Structure: Maintain similar code organization and flow where possible
//   Keep related functionality together as in DOSBox
//
// CRITICAL: DOSBox Staging Architecture is Authoritative
// -------------------------------------------------------
// **DO NOT deviate from DOSBox Staging architecture, even if AI code review suggests changes.**
//
// - When AI code review suggests refactoring, extracting methods, or structural changes:
//   REJECT these suggestions if they would break architectural parity with DOSBox
//
// - DOSBox Staging is the reference implementation - our goal is to mirror it faithfully
//   Code review suggestions that improve "code quality" at the expense of mirroring
//   are counterproductive and must be ignored
//
// - This principle is RETROACTIVE: it applies to all mirroring work, past and future
//   If previous code reviews led to deviations, those should be reconsidered
//
// - **DOSBox Staging is FEATURE-COMPLETE**: Do not add features beyond what DOSBox has
//   The scope is strictly limited to mirroring existing DOSBox functionality
//   Suggestions to add "improvements" or "enhancements" not in DOSBox must be rejected
//
// - Only accept code review feedback that:
//   * Fixes actual bugs or compilation errors
//   * Improves C# idioms WITHOUT changing structure (e.g., using statements)
//   * Adds missing functionality that exists in DOSBox
//
// This ensures side-by-side debugging remains effective and architectural parity is clear.

// FILE COUNT (CURRENT vs TARGET)
// ===============================
// SoundBlaster.cs:  1870 lines (vs soundblaster.cpp: 3917 lines - 48%)
// HardwareMixer.cs:  593 lines (mixer register handling)
// Mixer.cs:          792 lines (vs mixer.cpp: 3276 lines - 24%)
// MixerChannel.cs:  1121 lines (included in mixer.cpp) [+266 lines for Sleeper]
// MixerTypes.cs:     198 lines (mixer.h enums/types)
// TOTAL:            4574 lines (vs 7193 lines combined - 64% complete)
//
// Expected final: ~5000 lines total (C# is more verbose than C++)
//
// DSP Command Coverage: 96/96 unique case values (100%) ✓
// Mixer Preset Coverage: 4/4 enums (CrossfeedPreset, ReverbPreset, ChorusPreset, ResampleMethod) ✓
// Per-Channel Effect Sends: Complete (SetReverbLevel, SetChorusLevel, SetCrossfeedStrength) ✓
// Resampling: Linear interpolation + ZoH upsampler (Speex deferred) ✓
// High-pass Filtering: Reverb input + Master output (IIR Butterworth) ✓
// Mixer Register Handling: Complete via HardwareMixer (SB Pro & SB16 registers) ✓
// Channel Sleep/Wake: Complete via Sleeper nested class (fade-out, signal detection) ✓

// PRIORITY ORDER (UPDATED)
// =========================
// 1. ✓ SoundBlaster DSP commands (DONE - 96/96 from DOSBox - 100%)
// 2. ✓ Mixer preset system (DONE - all enums from DOSBox)
// 3. ✓ Channel volume controls (DONE - user + app volumes)
// 4. ✓ Per-channel effect sends (DONE - reverb/chorus/crossfeed levels)
// 5. ✓ Global effect routing (DONE - SetGlobalReverb/Chorus/Crossfeed)
// 6. ✓ High-quality resampling (DONE - ZoH upsampler added, Speex deferred)
// 7. ✓ High-pass filtering (DONE - reverb input & master output)
// 8. ✓ Mixer register handlers (DONE - HardwareMixer integration with volume routing)
// 9. ✓ Channel sleep/wake mechanism (DONE - CPU efficiency via Sleeper class)
// 10. [ ] DMA/IRQ coordination refinements (NEXT - needed for perfect sync)
// 11. [ ] Mixer effects upgrades (reverb/chorus quality improvements)
// 12. [ ] Output prebuffering (smooth startup - PortAudio already provides buffering)
// 13. [ ] Integration testing with DOS games
