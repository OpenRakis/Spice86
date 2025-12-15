// SPICE86 AUDIO PARITY PORT PLAN (UPDATED)
// ==========================================
// Port DOSBox Staging audio subsystem to achieve feature parity.
// Excludes: Fast-forward, Capture, ESFM, Speex (use IIR filters or similar for resampling)

// PHASE 1: SoundBlaster.cpp - Complete DSP Command Set [~99% COMPLETE]
// =====================================================================
// ✓ COMPLETED:
// - DAC class with rate measurement (lines 46-79)
// - ADPCM decoders (2/3/4-bit with step-size adaptation) (lines 199-303)
// - DSP command length tables (DspCommandLengthsSb/Sb16) (lines 397-437)
// - 95 unique DSP command case values (DOSBox has 96)
// - All commands verified to exist in DOSBox soundblaster.cpp
// - SbInfo structure with complete state management
// - E2 increment table for DMA identification
// - Bulk DMA read methods (ReadDma8Bit/ReadDma16Bit)
//
// REMAINING:
// - Add case 0x05 (SB16 ASP set codec parameter) - only missing command
// - Fine-tune DMA/IRQ event coordination with mixer callbacks
// - Mixer register read/write handlers (volume routing)
// - Speaker warmup timing refinements

// PHASE 2: Mixer.cpp - Core Mixing & Effects Pipeline [70% COMPLETE]
// ====================================================================
// ✓ COMPLETED:
// - Basic mixer thread loop with direct PortAudio output
// - Channel registry (ConcurrentDictionary)
// - Effect preset enums (Crossfeed, Reverb, Chorus) matching DOSBox
// - Effect configuration methods (Get/Set for each preset)
// - User and App volume controls per channel
// - Combined volume calculation (user * app * db0)
// - Linear interpolation upsampling (_doLerpUpsample)
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
//
// REMAINING:
// - Better resampling quality (currently linear interpolation only)
//   * Option 1: Port Speex-like algorithm
//   * Option 2: Use IIR filters for band-limited resampling
//   * Option 3: Polyphase resampler
// - High-pass filtering (IIR Butterworth available in Spice86.Libs)
// - Upgrade reverb to proper algorithm (MVerb-like or simple Schroeder)
// - Upgrade chorus to proper algorithm (TAL-Chorus-like with LFO)
// - Output prebuffering for smooth startup
// - Channel sleep/wake mechanism for CPU efficiency

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

// FILE COUNT (CURRENT vs TARGET)
// ===============================
// SoundBlaster.cs:  1741 lines (vs soundblaster.cpp: 3917 lines - 44%)
// Mixer.cs:         741 lines  (vs mixer.cpp: 3276 lines - 23%)
// MixerChannel.cs:  698 lines  (included in mixer.cpp)
// MixerTypes.cs:    198 lines  (mixer.h enums/types)
// TOTAL:            3378 lines (vs 7193 lines combined - 47% complete)
//
// Expected final: ~5000 lines total (C# is more verbose than C++)
//
// DSP Command Coverage: 95/96 unique case values (99%)
// Mixer Preset Coverage: 4/4 enums (CrossfeedPreset, ReverbPreset, ChorusPreset, ResampleMethod)
// Per-Channel Effect Sends: Complete (SetReverbLevel, SetChorusLevel, SetCrossfeedStrength)

// PRIORITY ORDER (UPDATED)
// =========================
// 1. ✓ SoundBlaster DSP commands (DONE - 95/96 from DOSBox)
// 2. ✓ Mixer preset system (DONE - all enums from DOSBox)
// 3. ✓ Channel volume controls (DONE - user + app volumes)
// 4. ✓ Per-channel effect sends (DONE - reverb/chorus/crossfeed levels)
// 5. ✓ Global effect routing (DONE - SetGlobalReverb/Chorus/Crossfeed)
// 6. [ ] High-quality resampling (NEXT - critical for audio quality)
// 7. [ ] DMA/IRQ coordination refinements (needed for perfect sync)
// 8. [ ] Mixer effects upgrades (reverb/chorus quality)
// 9. [ ] Output prebuffering (smooth startup)
// 10. [ ] Integration testing with DOS games
