# Audio Port Plan: Aligning Spice86 with DOSBox-Staging

## Overview
This document tracks the ongoing effort to align Spice86's audio subsystem with DOSBox-Staging's architecture for maximum fidelity and accuracy.

## Critical Architectural Difference

### Current Spice86 Architecture (INCORRECT)
```
Device (SB/OPL) → Resample → SoundChannel.Render() → SoftwareMixer.Render() → AudioPlayer
```
- Resampling happens BEFORE calling Render()
- Each device handles its own resampling
- Mixer just applies volume and writes to player

### DOSBox-Staging Architecture (CORRECT)
```
Device (SB/OPL) → MixerChannel.AddSamples() → [Convert+Resample+Filter] → audio_frames → Mix()
```
- Devices call AddSamples() with native-rate samples
- ALL resampling happens INSIDE AddSamples()
- Supports multiple resample methods (Lerp, ZOH, Speex)
- Applies filters and effects after resampling
- Mix() just pulls frames and mixes them

## Status: In Progress

### Phase 1: Core Infrastructure ✅
- [x] Analyze DOSBox-Staging architecture
- [x] Identify architectural differences
- [x] Create audio port plan document
- [ ] Add Speex resampler dependency/wrapper
- [ ] Create envelope processing for click removal
- [ ] Add filter infrastructure (IIR)

### Phase 2: MixerChannel Refactor 🔄
- [ ] Rename SoundChannel → MixerChannel
- [ ] Add audio_frames buffer (List<AudioFrame>)
- [ ] Add convert_buffer for sample conversion
- [ ] Implement AddSamples<T, stereo, signed, nativeorder>() template methods
- [ ] Implement ConvertSamplesAndMaybeZohUpsample()
- [ ] Implement ConvertNextFrame()
- [ ] Add prev_frame/next_frame tracking
- [ ] Add resampling logic (Lerp, ZOH, Speex)
- [ ] Add filter application (high-pass, low-pass, noise gate)
- [ ] Add crossfeed support
- [ ] Add volume gain handling (user_volume, app_volume, db0_volume, combined)
- [ ] Add channel/output mapping
- [ ] Add sleeper with fade-out support
- [ ] Implement Mix() that pulls from audio_frames

### Phase 3: SoftwareMixer Refactor 🔄
- [ ] Refactor to match DOSBox Mixer structure
- [ ] Remove resampling from Render() methods
- [ ] Implement proper frame mixing
- [ ] Add master gain support
- [ ] Add compressor support
- [ ] Add global crossfeed
- [ ] Add reverb (MVerb) support
- [ ] Add chorus (TAL-Chorus) support

### Phase 4: Sound Blaster Alignment ⏳
- [ ] Remove LinearUpsampler from SoundBlaster
- [ ] Remove Resample() method
- [ ] Change PlaybackLoopBody to call AddSamples() instead of Render()
- [ ] Pass native-rate samples to AddSamples()
- [ ] Verify DMA handling matches DOSBox
- [ ] Check mixer register implementation
- [ ] Verify ADPCM codec implementations
- [ ] Test with various SB games

### Phase 5: OPL3 Alignment ⏳
- [ ] Review OPL3 AudioCallback
- [ ] Ensure AddSamples_sfloat() is called correctly
- [ ] Verify 49716 Hz native sample rate
- [ ] Check AdLib Gold integration
- [ ] Verify timer handling
- [ ] Test with OPL3 games/demos

### Phase 6: Additional Audio Devices ⏳
- [ ] PC Speaker
- [ ] Gravis UltraSound
- [ ] MIDI devices
- [ ] Any other audio-producing devices

### Phase 7: Testing & Verification ⏳
- [ ] Unit tests for resampling
- [ ] Unit tests for sample conversion
- [ ] Integration tests with various sample rates
- [ ] Verify no audio glitches
- [ ] Compare output with DOSBox-Staging (if possible)
- [ ] Performance testing
- [ ] Regression testing with known games

### Phase 8: Documentation ✅
- [x] Create this audio port plan document
- [ ] Update inline code comments to match DOSBox style
- [ ] Document any intentional deviations
- [ ] Update architecture documentation

## Key Implementation Notes

### Resample Methods
1. **Resample**: High-quality Speex resampling (default)
2. **LerpUpsampleOrResample**: LERP upsample if needed, else Speex resample
3. **ZeroOrderHoldAndResample**: ZOH upsample to target rate, then Speex resample (emulates old DAC sound)

### Volume Gains
- **user_volume_gain**: Set via MIXER command (user control)
- **app_volume_gain**: Set programmatically by DOS app (e.g., SB mixer)
- **db0_volume_gain**: Brings channel to 0 dB in 16-bit range
- **combined_volume_gain**: Product of all three (applied in one multiply)

### Sample Conversion Flow
```
Raw samples → ConvertNextFrame → Apply volume gains → Envelope processing → 
Channel/output mapping → convert_buffer → Resampling → Filters → Crossfeed → audio_frames
```

### Frame vs Sample Terminology
- **Frame**: One time point across all channels (for stereo: 2 samples, one L and one R)
- **Sample**: One value for one channel
- DOSBox uses "frames" consistently; we should too

## Files Modified

### Core Audio
- `src/Spice86.Core/Emulator/Devices/Sound/SoftwareMixer.cs` - Major refactor
- `src/Spice86.Core/Emulator/Devices/Sound/SoundChannel.cs` - Rename to MixerChannel.cs, major refactor
- `src/Spice86.Core/Emulator/Devices/Sound/AudioEngine.cs` - Review
- `src/Spice86.Core/Emulator/Devices/Sound/AudioPlayerFactory.cs` - Review

### Sound Blaster
- `src/Spice86.Core/Emulator/Devices/Sound/Blaster/SoundBlaster.cs` - Remove resampling, change to AddSamples
- `src/Spice86.Core/Emulator/Devices/Sound/Blaster/Dsp.cs` - Review
- `src/Spice86.Core/Emulator/Devices/Sound/Blaster/LinearUpsampler.cs` - DEPRECATED (remove or mark obsolete)

### OPL
- `src/Spice86.Core/Emulator/Devices/Sound/Opl3Fm.cs` - Verify AddSamples usage

### New Files
- `src/Spice86.Core/Emulator/Devices/Sound/Resampling/SpeexResampler.cs` - Speex wrapper
- `src/Spice86.Core/Emulator/Devices/Sound/Resampling/ResampleMethod.cs` - Enum
- `src/Spice86.Core/Emulator/Devices/Sound/Processing/Envelope.cs` - Click removal
- `src/Spice86.Core/Emulator/Devices/Sound/Filtering/` - Filter classes

## Dependencies to Add
- Speex resampler (need to find C# wrapper or port)
- IIR filter library (already exists: Spice86.Libs.Sound.Filters.IirFilters)

## Intentional Deviations from DOSBox (if any)
_To be documented as they arise_

## Testing Strategy
1. Unit test each resampling method
2. Unit test sample conversion for all formats
3. Integration test with known audio output
4. Regression test with games that have audio issues
5. Performance benchmark to ensure no degradation

## Atomic Commits Strategy
Each commit should be:
1. A single logical change
2. Compilable and testable
3. Well-documented

Example commit sequence:
1. "Add Speex resampler wrapper"
2. "Add Envelope class for click removal"
3. "Add ResampleMethod enum and infrastructure"
4. "Refactor SoundChannel to MixerChannel with AddSamples"
5. "Remove resampling from SoundBlaster, use AddSamples"
6. "Add filter support to MixerChannel"
7. etc.

## References
- DOSBox-Staging: https://github.com/dosbox-staging/dosbox-staging
- Key files:
  - `src/audio/mixer.h`
  - `src/audio/mixer.cpp`
  - `src/hardware/audio/soundblaster.cpp`
  - `src/hardware/audio/opl.cpp`
