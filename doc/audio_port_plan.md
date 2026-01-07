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
- [x] Add ResampleMethod enum
- [x] Add LineIndex enum
- [x] Add StereoLine struct
- [x] Make AudioFrame public
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

## Detailed Implementation Guide

### Step-by-Step Refactoring Plan

#### 1. Create MixerChannel Class (New File)
Create `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs` with:

**Fields to add:**
```csharp
private readonly List<AudioFrame> _audioFrames = new();
private readonly List<AudioFrame> _convertBuffer = new();
private AudioFrame _prevFrame;
private AudioFrame _nextFrame;
private StereoLine _outputMap = StereoLine.StereoMap;
private StereoLine _channelMap = StereoLine.StereoMap;
private float _userVolumeGainLeft = 1.0f;
private float _userVolumeGainRight = 1.0f;
private float _appVolumeGainLeft = 1.0f;
private float _appVolumeGainRight = 1.0f;
private float _db0VolumeGain = 1.0f;
private float _combinedVolumeGainLeft = 1.0f;
private float _combinedVolumeGainRight = 1.0f;
private bool _lastSamplesWereStereo;
private ResampleMethod _resampleMethod = ResampleMethod.LerpUpsampleOrResample;
private int _mixerSampleRate = 48000; // Get from mixer
```

**Methods to implement:**
```csharp
// Main AddSamples methods (mirrors DOSBox)
public void AddSamples_m8(int numFrames, byte[] data)
public void AddSamples_m16(int numFrames, short[] data)
public void AddSamples_s16(int numFrames, short[] data)
public void AddSamples_mfloat(int numFrames, float[] data)
public void AddSamples_sfloat(int numFrames, float[] data)

// Internal implementation
private void AddSamplesInternal<T>(int numFrames, T[] data, bool stereo, bool signed, bool nativeOrder)
private void ConvertSamplesAndMaybeResample<T>(T[] data, int numFrames, bool stereo, bool signed)
private AudioFrame ConvertNextFrame<T>(T[] data, int pos, bool stereo, bool signed)
private void UpdateCombinedVolumeGain()
```

#### 2. Implement Sample Conversion
Following DOSBox's conversion logic:

**For 8-bit unsigned:**
- Silence value: 128
- Convert to float: `(sample - 128) / 128.0f`

**For 16-bit signed:**
- Silence value: 0
- Convert to float: `sample / 32768.0f`

**Apply volume gains:**
```csharp
frameWithGain.Left = prevFrame.Left * _combinedVolumeGainLeft;
frameWithGain.Right = prevFrame.Right * _combinedVolumeGainRight;
```

#### 3. Implement LERP Resampling in AddSamples
When channel sample rate != mixer sample rate and using LerpUpsampleOrResample:

```csharp
private struct LerpUpsamplerState {
    public float Pos;
    public float Step;
    public AudioFrame LastFrame;
}

// In AddSamplesInternal, after conversion:
if (_doLerpUpsample) {
    foreach (var frame in _convertBuffer) {
        while (_lerpState.Pos <= 1.0f) {
            AudioFrame lerped;
            lerped.Left = Lerp(_lerpState.LastFrame.Left, frame.Left, _lerpState.Pos);
            lerped.Right = Lerp(_lerpState.LastFrame.Right, frame.Right, _lerpState.Pos);
            _audioFrames.Add(lerped);
            _lerpState.Pos += _lerpState.Step;
        }
        _lerpState.Pos -= 1.0f;
        _lerpState.LastFrame = frame;
    }
}

private float Lerp(float a, float b, float t) => a + (b - a) * t;
```

#### 4. Update SoftwareMixer
Modify `Render` methods to NOT do resampling:

```csharp
// OLD (WRONG):
// Render() does: scale → resample → write to audio player

// NEW (CORRECT):
// Render() does: just write frames from _audioFrames to audio player
internal void Render(SoundChannel channel) {
    if (channel.Volume == 0 || channel.IsMuted) {
        _channels[channel].WriteSilence();
        return;
    }
    
    // Just write the pre-processed frames
    // (already scaled, resampled, filtered in AddSamples)
    _channels[channel].WriteData(channel._audioFrames);
    channel._audioFrames.Clear();
}
```

#### 5. Update Sound Blaster
In `SoundBlaster.cs`, change `PlaybackLoopBody()`:

```csharp
// OLD (WRONG):
private void PlaybackLoopBody() {
    _dsp.Read(_readFromDspBuffer);
    int length = Resample(_readFromDspBuffer, _outputSampleRate, _renderingBuffer); // ← WRONG
    PCMSoundChannel.Render(_renderingBuffer.AsSpan(0, length));
}

// NEW (CORRECT):
private void PlaybackLoopBody() {
    _dsp.Read(_readFromDspBuffer);
    
    // Determine sample format and call appropriate AddSamples method
    if (_dsp.Is16Bit && _dsp.IsStereo) {
        int numFrames = _readFromDspBuffer.Length / 4; // 16-bit stereo = 4 bytes per frame
        PCMSoundChannel.AddSamples_s16(numFrames, 
            MemoryMarshal.Cast<byte, short>(_readFromDspBuffer).ToArray());
    } else if (_dsp.Is16Bit) {
        int numFrames = _readFromDspBuffer.Length / 2; // 16-bit mono = 2 bytes per frame
        PCMSoundChannel.AddSamples_m16(numFrames,
            MemoryMarshal.Cast<byte, short>(_readFromDspBuffer).ToArray());
    } else if (_dsp.IsStereo) {
        int numFrames = _readFromDspBuffer.Length / 2; // 8-bit stereo = 2 bytes per frame
        PCMSoundChannel.AddSamples_m8(numFrames, _readFromDspBuffer);
    } else {
        int numFrames = _readFromDspBuffer.Length; // 8-bit mono = 1 byte per frame
        PCMSoundChannel.AddSamples_m8(numFrames, _readFromDspBuffer);
    }
}
```

#### 6. Mark LinearUpsampler as Obsolete
Add `[Obsolete("Resampling now happens in MixerChannel.AddSamples")]` to LinearUpsampler class.

#### 7. Testing Strategy
1. Build and ensure no compilation errors
2. Test with a simple SB program (e.g., Sound Blaster test utility)
3. Verify audio plays without glitches
4. Compare behavior with DOSBox-Staging if possible
5. Run regression tests with audio-heavy games

### Critical Implementation Notes

**Volume Gain Calculation:**
```csharp
// User volume (0-100%) → gain (0.0-1.0)
_userVolumeGainLeft = Volume / 100.0f;
_userVolumeGainRight = Volume / 100.0f;

// Combined gain (multiply all gains together)
_combinedVolumeGainLeft = _userVolumeGainLeft * _appVolumeGainLeft * _db0VolumeGain;
_combinedVolumeGainRight = _userVolumeGainRight * _appVolumeGainRight * _db0VolumeGain;
```

**Channel and Output Mapping:**
```csharp
// When converting samples:
var mappedChannelLeft = _channelMap.Left;  // Which input channel goes to left
var mappedChannelRight = _channelMap.Right; // Which input channel goes to right

// After volume gain:
AudioFrame outFrame;
outFrame[_outputMap.Left] = frameWithGain.Left;   // Where left goes in output
outFrame[_outputMap.Right] = frameWithGain.Right; // Where right goes in output
```

**Resample Step Calculation:**
```csharp
_lerpState.Step = (float)_mixerSampleRate / SampleRate;
```

### Known Limitations of Initial Implementation

1. **No Speex Resampler**: Using LERP only initially
   - LERP is acceptable for upsampling
   - For production, should add Speex via P/Invoke or port
   
2. **No ZOH Upsampling**: Can be added later
   - Emulates old DAC sound
   - Lower priority than getting architecture right

3. **No Filters**: High-pass, low-pass, noise gate
   - Can be added incrementally after core architecture is working
   
4. **No Crossfeed/Reverb/Chorus**: Advanced effects
   - These are enhancements, not architectural requirements

### Success Criteria

1. ✅ Audio devices call AddSamples() with native-rate samples
2. ✅ Resampling happens INSIDE AddSamples, not before
3. ✅ Code structure mirrors DOSBox-Staging flow
4. ✅ No audio glitches or dropouts
5. ✅ Linear upsampling works correctly
6. ✅ Volume control works
7. ✅ Stereo/mono handling works
8. ✅ Build succeeds with no warnings
