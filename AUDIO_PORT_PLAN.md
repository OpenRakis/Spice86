// SPICE86 AUDIO PARITY PORT PLAN
// ==============================
// Port DOSBox Staging audio subsystem to achieve feature parity.
// Excludes: Fast-forward, Capture, ESFM, Speex (use existing resampling)

// PHASE 1: SoundBlaster.cpp - Complete DSP Command Set
// =====================================================
// Current gaps:
// - Missing DSP command handlers (0x00-0xFF)
// - Incomplete DMA mode handling (ADPCM2/3/4-bit, 8-bit PCM, 16-bit PCM)
// - Missing DAC rate measurement
// - Missing ADPCM decoding functions
// - Incomplete Mixer state management (input/output volumes, stereo routing)
// - Missing IRQ/DMA event coordination

// Key structures to add:
// 1. Dac class - rate measurement via write timing
// 2. ADPCM decoders (2-bit, 3-bit, 4-bit with step-size adaptation)
// 3. DSP command handlers for all 256 commands (command_table approach)
// 4. DMA transfer processing with proper byte counting
// 5. IRQ/DMA synchronization events
// 6. Mixer register implementation (volume controls, input/output routing)

// PHASE 2: Mixer.cpp - Core Mixing & Effects Pipeline
// ====================================================
// Current gaps:
// - Missing resampling (DOSBox uses Speex; Spice86 needs own approach)
// - Missing output buffering logic
// - Missing reverb/chorus/crossfeed/compressor integration
// - Missing high-pass filter
// - Missing channel normalization/gain adjustment
// - Missing final output pipeline

// Key structures to add:
// 1. Resampling filters (using IIR filters you already have)
// 2. Effect pipeline (reverb, chorus, crossfeed, compressor)
// 3. Master gain/compression
// 4. Output buffer management
// 5. Prebuffering system (smooth startup)

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

// FILE COUNT
// ==========
// soundblaster.cpp: 3918 lines (full port needed: ~2800 lines after stripping C++ specifics)
// mixer.cpp: 3280 lines (core mixing: ~1500 lines, effects: ~800 lines, buffers: ~300 lines)
// Expected Spice86 result: ~4000-4500 lines combined (C# verbose vs C++ compact)

// PRIORITY ORDER
// ==============
// 1. SoundBlaster DSP commands (affects game compatibility immediately)
// 2. DMA/IRQ coordination (affects Dune audio sync)
// 3. Mixer resampling (affects audio quality)
// 4. Mixer effects (affects audio quality perception)
// 5. Mixer output pipeline (final polish)
