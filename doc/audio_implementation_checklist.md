# Implementation Checklist - Audio Architecture Alignment

This checklist tracks the implementation of DOSBox-Staging audio architecture in Spice86.

## Phase 1: Infrastructure ✅ COMPLETE
- [x] Clone DOSBox-Staging repository
- [x] Analyze mixer architecture  
- [x] Identify architectural differences
- [x] Create ResampleMethod enum
- [x] Create LineIndex enum
- [x] Create StereoLine struct
- [x] Make AudioFrame public
- [x] Create comprehensive documentation

## Phase 2: MixerChannel Class 🔄 IN PROGRESS

### 2.1 Create MixerChannel.cs ⏳
Location: `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs`

**Required Fields:**
```csharp
- List<AudioFrame> _audioFrames
- List<AudioFrame> _convertBuffer  
- AudioFrame _prevFrame, _nextFrame
- StereoLine _outputMap, _channelMap
- Volume gains (user, app, db0, combined)
- ResampleMethod _resampleMethod
- Lerp upsampler state (pos, step, lastFrame)
- int _mixerSampleRate
```

### 2.2 Implement AddSamples Methods ⏳
```csharp
- [ ] AddSamples_m8(int numFrames, byte[] data)
- [ ] AddSamples_m16(int numFrames, short[] data)
- [ ] AddSamples_s16(int numFrames, short[] data)
- [ ] AddSamples_mfloat(int numFrames, float[] data)
- [ ] AddSamples_sfloat(int numFrames, float[] data)
```

### 2.3 Implement Internal Methods ⏳
```csharp
- [ ] AddSamplesInternal<T>() - Template method
- [ ] ConvertSamplesAndMaybeResample<T>() - Sample conversion
- [ ] ConvertNextFrame<T>() - Single frame conversion  
- [ ] ApplyLerpResampling() - LERP interpolation
- [ ] UpdateCombinedVolumeGain() - Volume calculation
```

### 2.4 Test MixerChannel ⏳
- [ ] Unit test: 8-bit mono conversion
- [ ] Unit test: 8-bit stereo conversion
- [ ] Unit test: 16-bit mono conversion
- [ ] Unit test: 16-bit stereo conversion
- [ ] Unit test: LERP upsampling 22050→48000
- [ ] Unit test: LERP upsampling 44100→48000
- [ ] Unit test: Volume gain application
- [ ] Unit test: Channel mapping
- [ ] Unit test: Output mapping

## Phase 3: SoftwareMixer Update ⏳

### 3.1 Refactor SoftwareMixer ⏳
- [ ] Remove resampling from Render(Span<float>)
- [ ] Remove resampling from Render(Span<short>)
- [ ] Remove resampling from Render(Span<byte>)
- [ ] Add GetAudioFrames(MixerChannel) method
- [ ] Update Register() to work with MixerChannel

### 3.2 Test SoftwareMixer ⏳
- [ ] Unit test: Render without resampling
- [ ] Integration test: MixerChannel + SoftwareMixer

## Phase 4: Sound Blaster Update ⏳

### 4.1 Update SoundBlaster.cs ⏳
- [ ] Modify PlaybackLoopBody() to call AddSamples
- [ ] Remove Resample() method calls
- [ ] Detect format (8/16-bit, mono/stereo)
- [ ] Call appropriate AddSamples_* method
- [ ] Remove _renderingBuffer conversion

### 4.2 Deprecate LinearUpsampler ⏳
- [ ] Add [Obsolete] attribute to LinearUpsampler
- [ ] Add comment explaining replacement
- [ ] Keep for backward compatibility (short term)

### 4.3 Test Sound Blaster ⏳
- [ ] Test 8-bit mono playback
- [ ] Test 8-bit stereo playback
- [ ] Test 16-bit mono playback
- [ ] Test 16-bit stereo playback
- [ ] Test DMA auto-init mode
- [ ] Test various sample rates (11025, 22050, 44100)
- [ ] Test with real DOS program (e.g., sbtest.exe)

## Phase 5: OPL3 Update ⏳

### 5.1 Verify Opl3Fm.cs ⏳
- [ ] Check AudioCallback uses AddSamples_sfloat
- [ ] Verify 49716 Hz native rate
- [ ] Check frame rendering logic
- [ ] Verify AdLib Gold integration

### 5.2 Test OPL3 ⏳
- [ ] Test OPL2 music playback
- [ ] Test OPL3 music playback
- [ ] Test AdLib Gold if enabled
- [ ] Test with OPL music in DOS game

## Phase 6: Additional Devices ⏳

### 6.1 PC Speaker ⏳
- [ ] Review PcSpeaker.cs
- [ ] Ensure uses AddSamples if needed
- [ ] Test PC speaker sound

### 6.2 Gravis UltraSound ⏳
- [ ] Review GravisUltraSound.cs
- [ ] Update to use AddSamples
- [ ] Test GUS playback

### 6.3 Other Audio Devices ⏳
- [ ] Review all IRequestInterrupt implementers
- [ ] Update any that produce audio
- [ ] Test each device

## Phase 7: Integration Testing ⏳

### 7.1 Regression Tests ⏳
- [ ] Run existing audio tests
- [ ] Test with Dune (Cryogenic project)
- [ ] Test with games using SB audio
- [ ] Test with games using OPL music
- [ ] Test with games using both

### 7.2 Performance Testing ⏳
- [ ] Benchmark audio processing overhead
- [ ] Check for audio dropouts
- [ ] Profile resampling performance
- [ ] Compare with old implementation

### 7.3 Compatibility Testing ⏳
- [ ] Test on Windows
- [ ] Test on Linux  
- [ ] Test on macOS (if supported)
- [ ] Test various audio configurations

## Phase 8: Advanced Features ⏳ (Optional)

### 8.1 Speex Resampler ⏳
- [ ] Research Speex P/Invoke options
- [ ] Create SpeexResampler wrapper
- [ ] Add to MixerChannel
- [ ] Add ResampleMethod.Resample support
- [ ] Test high-quality resampling

### 8.2 ZOH Upsampler ⏳
- [ ] Implement zero-order-hold upsampling
- [ ] Add ZOH state to MixerChannel
- [ ] Add ResampleMethod.ZeroOrderHoldAndResample
- [ ] Test DAC emulation sound

### 8.3 Filters ⏳
- [ ] Add high-pass filter support
- [ ] Add low-pass filter support
- [ ] Add noise gate support
- [ ] Integrate IIR filter library
- [ ] Test filter application

### 8.4 Effects ⏳
- [ ] Add crossfeed support
- [ ] Add reverb support (MVerb)
- [ ] Add chorus support (TAL-Chorus)
- [ ] Add compressor support
- [ ] Test effects

## Phase 9: Cleanup and Documentation ⏳

### 9.1 Code Cleanup ⏳
- [ ] Remove LinearUpsampler entirely
- [ ] Remove old Render methods from SoundChannel
- [ ] Clean up unused code
- [ ] Run code analysis
- [ ] Fix any warnings

### 9.2 Documentation ⏳
- [ ] Update XML documentation
- [ ] Add inline comments matching DOSBox style
- [ ] Update architecture documentation
- [ ] Document any intentional deviations
- [ ] Create migration guide

### 9.3 Final Verification ⏳
- [ ] Code review
- [ ] Final testing round
- [ ] Compare behavior with DOSBox-Staging
- [ ] Update this checklist as complete

## Notes

### Current Status
- **Last Updated:** 2026-01-07
- **Current Phase:** Phase 2 (MixerChannel Creation)
- **Blockers:** None
- **Next Action:** Implement MixerChannel.AddSamples methods

### Key References
- `doc/audio_port_plan.md` - Detailed implementation guide
- `doc/audio_alignment_summary.md` - Quick reference
- DOSBox-Staging: `/tmp/dosbox-staging/src/audio/mixer.cpp` lines 2125-2268

### Success Criteria
✅ All checklist items completed
✅ All tests passing
✅ No audio glitches
✅ Architecture matches DOSBox-Staging
✅ Performance acceptable
✅ Documentation complete
