# Phase 4: Mixer Method Mapping
## DOSBox Staging → Spice86 Mirroring Plan

**Document Created:** 2025-12-15  
**Status:** Analysis Complete, Ready for Implementation

---

## Executive Summary

This document provides a comprehensive method-by-method mapping between DOSBox Staging's `mixer.cpp` (3276 lines) and Spice86's `Mixer.cs` (792 lines). With DOSBox source now available at `/tmp/dosbox-staging/`, we can proceed with Phase 4 (Core Mixer Thread Architecture) that was previously blocked.

**Key Finding:** Reverb/Chorus "upgrades" mentioned in AUDIO_PORT_PLAN.md are NOT feature additions - they are legitimate mirroring tasks. DOSBox Staging uses:
- **MVerb** (Martin Eastwood Reverb) - Professional algorithmic reverb
- **TAL-Chorus** (Togu Audio Line Chorus) - Studio-quality chorus with LFO

These are located in:
- `/tmp/dosbox-staging/src/libs/mverb/MVerb.h`
- `/tmp/dosbox-staging/src/libs/tal-chorus/ChorusEngine.h`

---

## Current Status Analysis

### What Spice86 Already Has ✅

#### Core Mixer Infrastructure (792 lines)
1. **MixerThreadLoop()** - Mirrors `mixer_thread_loop()` (DOSBox lines 2607-2715)
   - Continuous loop requesting frames
   - Direct PortAudio output (vs DOSBox's SDL)
   - Lock-based thread safety

2. **MixSamples()** - Mirrors `mix_samples()` (DOSBox lines 2396-2540)
   - Channel accumulation loop
   - Reverb/chorus aux buffers
   - Effect sends with per-channel gain
   - Master gain application
   - High-pass filtering (reverb input + master output)
   - Compressor and normalization

3. **Effect Pipeline**
   - Basic reverb (50ms delay buffer + feedback)
   - Basic chorus (20ms delay buffer)
   - Crossfeed (30% stereo mix)
   - Compressor (4:1 ratio, attack/release envelope)
   - Peak tracking normalization

4. **Channel Management**
   - `RegisterChannel()` / `DeregisterChannel()`
   - `FindChannel()` / `GetAllChannels()`
   - Thread-safe ConcurrentDictionary

5. **Volume Controls**
   - Master gain (atomic AudioFrame)
   - Per-channel user/app volumes (in MixerChannel.cs)

6. **High-Pass Filtering**
   - 2nd-order Butterworth filters (120Hz for reverb, 3Hz for master)
   - Matches DOSBox implementation

### What DOSBox Has That Spice86 Doesn't ❌

#### 1. Advanced Reverb - MVerb Algorithm (~150 lines)
**DOSBox Location:** `src/libs/mverb/MVerb.h` + `mixer.cpp` lines 78-125, 2445-2467

**Current Spice86:** Simple delay + feedback (lines 694-722)

**What's Missing:**
- MVerb class with 11 parameters:
  - PREDELAY, EARLYMIX, SIZE, DENSITY
  - BANDWIDTHFREQ, DECAY, DAMPINGFREQ
  - GAIN, MIX (always 1.0 in DOSBox)
- Multi-stage FDN (Feedback Delay Network) architecture
- Early reflections modeling
- Late reverberation with density control
- Frequency-dependent damping
- Separate highpass filter on reverb input (already implemented in Spice86!)

**DOSBox Usage:**
```cpp
// Setup reverb with preset values
reverb.Setup(
    predelay: 0.02f,      // 20ms predelay
    early_mix: 0.4f,       // 40% early reflections
    size: 0.75f,           // Large room
    density: 0.5f,         // Medium density
    bandwidth_freq_hz: 8000.0f,  // Highpass for input
    decay: 0.5f,           // Medium decay time
    dampening_freq_hz: 5000.0f,  // Lowpass in feedback
    synth_level: 0.25f,    // 25% send for synth channels
    digital_level: 0.15f,  // 15% send for digital audio
    highpass_freq_hz: 120.0f,  // Already in Spice86!
    sample_rate_hz: 48000
);

// Per-frame processing (non-interleaved)
float* in_buf[2] = {&in_frame.left, &in_frame.right};
float* out_buf[2] = {&out_frame.left, &out_frame.right};
mixer.reverb.mverb.process(in_buf, out_buf, 1);
```

**Implementation Strategy:**
- Port MVerb.h header file to C# class (template-based, parameterized on float)
- Create MVerb.cs with all 11 parameters
- Implement FDN architecture (delay lines, diffusion, damping)
- Replace Spice86's `ApplyReverb()` to use MVerb.Process()
- Keep existing highpass filter integration (already correct!)

**Estimated Effort:** 4-6 hours (straightforward template→class port)

#### 2. Advanced Chorus - TAL-Chorus Algorithm (~200 lines)
**DOSBox Location:** `src/libs/tal-chorus/` + `mixer.cpp` lines 127-151, 2470-2478

**Current Spice86:** Simple fixed delay (lines 728-747)

**What's Missing:**
- ChorusEngine class with LFO (Low-Frequency Oscillator)
- Dual chorus lines (Chorus1 and Chorus2, only Chorus1 enabled in DOSBox)
- Modulated delay (LFO controls delay time for pitch variation)
- Interpolated delay reads for smooth modulation
- DC blocking filter
- Feedback and wet/dry controls

**TAL-Chorus Components:**
- `Lfo.h` / `Lfo.cpp` - Sine/triangle wave oscillator
- `OnePoleLP.h` - Simple lowpass filter
- `OscNoise.h` - Noise generator for randomization
- `DCBlock.h` - DC offset removal
- `Chorus.h` - Single chorus line with modulation
- `ChorusEngine.h` - Dual chorus controller

**DOSBox Usage:**
```cpp
// Setup chorus
chorus.Setup(
    synth_level: 0.25f,     // 25% send for synth channels
    digital_level: 0.15f,   // 15% send for digital audio
    sample_rate_hz: 48000
);
chorus_engine.setEnablesChorus(true, false);  // Chorus1 only

// Per-frame processing (interleaved, in-place)
chorus_engine.process(&frame.left, &frame.right);
```

**Implementation Strategy:**
- Port TAL-Chorus C++ classes to C# (5 support classes + 2 main classes)
- Create `Lfo.cs`, `OnePoleLP.cs`, `OscNoise.cs`, `DCBlock.cs`
- Create `Chorus.cs` and `ChorusEngine.cs`
- Replace Spice86's `ApplyChorus()` to use ChorusEngine.Process()
- Configure for Chorus1-only mode (matches DOSBox)

**Estimated Effort:** 8-12 hours (multiple interdependent classes)

#### 3. Professional Compressor Class (~100 lines)
**DOSBox Location:** `src/audio/private/compressor.h` + `mixer.cpp` lines 659-688, 2493-2498

**Current Spice86:** Inline peak detection + gain reduction (lines 605-638)

**What's Missing:**
- Separate Compressor class with configurable parameters
- RMS-based detection (vs simple peak detection)
- Proper attack/release envelopes (time-based, not coefficient-based)
- Knee width control (hard knee vs soft knee)
- Makeup gain
- Sidechain support

**DOSBox Compressor API:**
```cpp
class Compressor {
public:
    void Configure(float threshold_db, float ratio,
                  float attack_ms, float release_ms,
                  float knee_width_db, float makeup_gain_db,
                  int sample_rate_hz);
    AudioFrame Process(AudioFrame input);
private:
    float rms_detector;
    float envelope_follower;
    // ... internal state
};
```

**Current Spice86 Approach:**
- Simple peak tracking with attack/release coefficients
- Works but less sophisticated than DOSBox

**Implementation Strategy:**
- Port `compressor.h` to `Compressor.cs`
- Implement RMS detection windowing
- Add time-based attack/release (milliseconds → samples)
- Add knee width and makeup gain
- Replace inline compressor code with Compressor.Process()

**Estimated Effort:** 3-4 hours (self-contained class)

#### 4. Preset Configuration System (~300 lines)
**DOSBox Location:** `mixer.cpp` lines 378-658

**Current Spice86:** Enum-based presets, no configuration methods

**What's Missing:**
```cpp
// Crossfeed preset parsing and configuration
CrossfeedPreset crossfeed_pref_to_preset(const std::string& pref);
const char* to_string(const CrossfeedPreset preset);
void sync_crossfeed_setting(const CrossfeedPreset preset);
CrossfeedPreset MIXER_GetCrossfeedPreset();
void MIXER_SetCrossfeedPreset(const CrossfeedPreset new_preset);

// Reverb preset parsing and configuration
ReverbPreset reverb_pref_to_preset(const std::string& pref);
const char* to_string(const ReverbPreset preset);
void sync_reverb_setting(const ReverbPreset preset);
ReverbPreset MIXER_GetReverbPreset();
void MIXER_SetReverbPreset(const ReverbPreset new_preset);

// Chorus preset parsing and configuration
ChorusPreset chorus_pref_to_preset(const std::string& pref);
const char* to_string(const ChorusPreset preset);
void sync_chorus_setting(const ChorusPreset preset);
ChorusPreset MIXER_GetChorusPreset();
void MIXER_SetChorusPreset(const ChorusPreset new_preset);
```

**Implementation Strategy:**
- Add preset parsing from string (for config/CLI)
- Add preset→string conversion (for logging/display)
- Add SetXXXPreset() public API methods
- Add GetXXXPreset() query methods
- Wire up to effect initialization (init_compressor, setup reverb/chorus)

**Estimated Effort:** 4-6 hours (mostly straightforward enum handling)

#### 5. Global Effect Send Helpers (~100 lines)
**DOSBox Location:** `mixer.cpp` lines 333-377

**Current Spice86:** No global send configuration

**What's Missing:**
```cpp
// Set effect send levels for ALL channels
static void set_global_crossfeed(const MixerChannelPtr& channel);
static void set_global_reverb(const MixerChannelPtr& channel);
static void set_global_chorus(const MixerChannelPtr& channel);
```

These iterate over all channels and configure effect sends based on channel type (synthesizer vs digital audio) and preset settings.

**Implementation Strategy:**
- Add `SetGlobalCrossfeed()`, `SetGlobalReverb()`, `SetGlobalChorus()` methods
- Determine channel type (synth vs digital) - may need to add ChannelType enum
- Apply preset-specific send levels to each channel
- Call when preset changes

**Estimated Effort:** 2-3 hours (utility methods)

#### 6. Channel Feature Queries (~50 lines)
**DOSBox Location:** `mixer.cpp` lines 316-327

**Current Spice86:** Partial implementation in MixerChannel.cs

**What's Missing:**
```cpp
bool MixerChannel::HasFeature(const ChannelFeature feature);
std::set<ChannelFeature> MixerChannel::GetFeatures();
```

**Note:** This is mostly in MixerChannel.cs, may just need public API exposure.

**Estimated Effort:** 1 hour (minor additions)

#### 7. Configuration/Setup Integration (~150 lines)
**DOSBox Location:** `mixer.cpp` lines 3080-3267

**Status:** OUT OF SCOPE (excluded per AUDIO_PORT_PLAN.md)

Spice86 uses different configuration approach (CLI args + Configuration class), so DOSBox's config file parsing is not needed.

---

## Implementation Plan

### Phase 4.1: Advanced Effects Algorithms (Priority: HIGH)
**Estimated Effort:** 15-22 hours

1. **MVerb Integration** (4-6 hours)
   - Port MVerb.h to MVerb.cs
   - Implement FDN reverb architecture
   - Update ApplyReverb() to use MVerb
   - Test with various room sizes/decay times

2. **TAL-Chorus Integration** (8-12 hours)
   - Port TAL-Chorus library files (Lfo, Chorus, ChorusEngine, etc.)
   - Implement modulated delay with LFO
   - Update ApplyChorus() to use ChorusEngine
   - Test with various modulation depths/rates

3. **Compressor Upgrade** (3-4 hours)
   - Port compressor.h to Compressor.cs
   - Implement RMS detection and envelope follower
   - Add knee width and makeup gain
   - Replace inline compressor with class

### Phase 4.2: Preset System (Priority: MEDIUM)
**Estimated Effort:** 6-9 hours

1. **Preset Configuration Methods** (4-6 hours)
   - Add XXX_pref_to_preset() parsing
   - Add to_string() conversions
   - Add Set/Get public API methods
   - Wire up to effect initialization

2. **Global Effect Sends** (2-3 hours)
   - Add SetGlobalXXX() helper methods
   - Implement channel type detection
   - Apply preset-specific send levels

### Phase 4.3: Minor Enhancements (Priority: LOW)
**Estimated Effort:** 1-2 hours

1. **Channel Feature Queries** (1 hour)
   - Expose HasFeature() / GetFeatures() publicly
   - Ensure compatibility with DOSBox API

---

## Success Criteria

### Functional Parity
- [ ] MVerb reverb produces professional algorithmic reverberation
- [ ] TAL-Chorus produces smooth modulated chorus effect
- [ ] Compressor provides RMS-based dynamic range control
- [ ] Preset system allows easy effect configuration
- [ ] Global sends configure all channels at once

### Code Quality
- [ ] Zero compilation warnings
- [ ] All methods documented with DOSBox references
- [ ] Side-by-side verification possible with DOSBox source
- [ ] No deviation from DOSBox architecture

### Testing
- [ ] Build succeeds without errors
- [ ] Basic audio playback works correctly
- [ ] Effects can be enabled/disabled without artifacts
- [ ] Preset changes apply correctly to all channels

---

## Blockers Resolved

✅ **DOSBox Source Availability** - Now available at `/tmp/dosbox-staging/`  
✅ **Reverb/Chorus Scope Concern** - Confirmed as legitimate mirroring, not feature addition

## Next Steps

1. **Immediate:** Begin Phase 4.1 with MVerb port
2. **After MVerb:** Port TAL-Chorus library
3. **After TAL-Chorus:** Upgrade Compressor class
4. **Final:** Add preset system and global sends

---

## References

- **DOSBox Mixer:** `/tmp/dosbox-staging/src/audio/mixer.cpp` (3276 lines)
- **MVerb Library:** `/tmp/dosbox-staging/src/libs/mverb/MVerb.h`
- **TAL-Chorus Library:** `/tmp/dosbox-staging/src/libs/tal-chorus/`
- **Compressor:** `/tmp/dosbox-staging/src/audio/private/compressor.h`
- **Planning:** `/home/runner/work/Spice86/Spice86/AUDIO_PORT_PLAN.md`
- **Next Steps:** `/home/runner/work/Spice86/Spice86/docs/audio/NEXT_STEPS.md`

---

**Last Updated:** 2025-12-15  
**Status:** Analysis Complete, Ready for Implementation  
**Estimated Total Effort:** 22-33 hours for complete Phase 4
