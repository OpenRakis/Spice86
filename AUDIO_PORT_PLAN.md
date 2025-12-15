// SPICE86 AUDIO PARITY PORT PLAN (UPDATED)
// ==========================================
// Port DOSBox Staging audio subsystem to achieve feature parity.
// Excludes: Fast-forward, Capture, ESFM, Speex (use IIR filters or similar for resampling)

// PHASE 1: SoundBlaster.cpp - Complete DSP Command Set [90% COMPLETE]
// ====================================================================
// ✓ COMPLETED:
// - DAC class with rate measurement (lines 46-79)
// - ADPCM decoders (2/3/4-bit with step-size adaptation) (lines 199-303)
// - DSP command length tables (DspCommandLengthsSb/Sb16) (lines 397-437)
// - 111 DSP command case handlers (vs 77 in DOSBox - expanded coverage)
// - SbInfo structure with complete state management
// - E2 increment table for DMA identification
// - Bulk DMA read methods (ReadDma8Bit/ReadDma16Bit)
//
// REMAINING:
// - Fine-tune DMA/IRQ event coordination with mixer callbacks
// - Mixer register read/write handlers (volume routing)
// - Speaker warmup timing refinements

// PHASE 2: Mixer.cpp - Core Mixing & Effects Pipeline [60% COMPLETE]
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
// SoundBlaster.cs:  1772 lines (vs soundblaster.cpp: 3917 lines)
// Mixer.cs:         640 lines  (vs mixer.cpp: 3276 lines)
// MixerChannel.cs:  469 lines  (included in mixer.cpp)
// MixerTypes.cs:    198 lines  (mixer.h enums/types)
// TOTAL:            3079 lines (vs 7193 lines combined - 43% complete)
//
// Expected final: ~5000 lines total (C# is more verbose than C++)

// PRIORITY ORDER (UPDATED)
// =========================
// 1. ✓ SoundBlaster DSP commands (DONE - 111 handlers)
// 2. ✓ Mixer preset system (DONE - enums + configuration)
// 3. ✓ Channel volume controls (DONE - user + app volumes)
// 4. [ ] High-quality resampling (NEXT - critical for audio quality)
// 5. [ ] DMA/IRQ coordination refinements (needed for perfect sync)
// 6. [ ] Mixer effects upgrades (reverb/chorus quality)
// 7. [ ] Output prebuffering (smooth startup)
// 8. [ ] Integration testing with DOS games
