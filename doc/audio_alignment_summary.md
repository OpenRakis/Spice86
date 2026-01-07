# Audio Architecture Alignment - Summary

## Critical Issue Identified

**Spice86's audio architecture does NOT match DOSBox-Staging**

### Current (INCORRECT) Architecture:
```
Audio Device (SB/OPL) 
  → Reads samples at native rate
  → RESAMPLES using LinearUpsampler ❌ 
  → Calls SoundChannel.Render(resampled_data)
  → SoftwareMixer.Render() applies volume and writes to AudioPlayer
```

### DOSBox-Staging (CORRECT) Architecture:
```
Audio Device (SB/OPL)
  → Produces samples at native rate
  → Calls MixerChannel.AddSamples(native_rate_data) ✅
  → INSIDE AddSamples:
     - Convert samples to float
     - Apply volume gains
     - RESAMPLE to mixer rate ✅
     - Apply filters/crossfeed
     - Store in audio_frames buffer
  → Mix() pulls from audio_frames and mixes to output
```

## Why This Matters

1. **Architectural Fidelity**: Spice86 must match DOSBox-Staging exactly for accurate emulation
2. **Extensibility**: The DOSBox architecture allows easy addition of filters, effects, and multiple resample methods
3. **Correctness**: Resampling should happen as part of the mixer pipeline, not in device code

## Solution Overview

The `doc/audio_port_plan.md` contains:
- Complete 9-phase implementation plan
- Detailed step-by-step instructions
- Code examples for all changes
- Testing strategy
- Success criteria

## Key Changes Required

1. **Create MixerChannel** with AddSamples methods (replaces SoundChannel.Render)
2. **Move resampling INTO AddSamples** (remove from device code)
3. **Update Sound Blaster** to call AddSamples with native-rate samples
4. **Update OPL3** to call AddSamples correctly
5. **Deprecate LinearUpsampler** (resampling now in MixerChannel)

## Files to Modify

### New Files:
- ✅ `src/Spice86.Core/Emulator/Devices/Sound/ResampleMethod.cs`
- ✅ `src/Spice86.Core/Emulator/Devices/Sound/LineIndex.cs`  
- ✅ `src/Spice86.Core/Emulator/Devices/Sound/StereoLine.cs`
- 🔄 `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs` (to be created)

### Modified Files:
- ✅ `src/Spice86.Libs/Sound/Common/AudioFrame.cs` (made public)
- ✅ `doc/audio_port_plan.md` (comprehensive plan)
- 🔄 `src/Spice86.Core/Emulator/Devices/Sound/SoftwareMixer.cs`
- 🔄 `src/Spice86.Core/Emulator/Devices/Sound/Blaster/SoundBlaster.cs`
- 🔄 `src/Spice86.Core/Emulator/Devices/Sound/Blaster/LinearUpsampler.cs` (deprecate)
- 🔄 `src/Spice86.Core/Emulator/Devices/Sound/Opl3Fm.cs`

Legend: ✅ Done | 🔄 To be modified

## Current Status

**Analysis and Planning: COMPLETE** ✅
- DOSBox-Staging architecture fully analyzed
- Architectural difference identified and documented
- Comprehensive implementation plan created
- Infrastructure types added
- All commits are atomic and well-documented

**Implementation: TO BE DONE** 🔄
- Follow the detailed guide in `doc/audio_port_plan.md`
- Each step should be a separate atomic commit
- Test after each major change

## Next Steps for Developer

1. Read `doc/audio_port_plan.md` thoroughly
2. Implement MixerChannel with AddSamples methods
3. Test with a simple audio program
4. Update Sound Blaster to use AddSamples
5. Update OPL3 to use AddSamples
6. Run regression tests
7. Mark LinearUpsampler as obsolete

## References

- DOSBox-Staging Repository: https://github.com/dosbox-staging/dosbox-staging
- Key Reference Files:
  - `src/audio/mixer.h` - MixerChannel class definition
  - `src/audio/mixer.cpp` - AddSamples implementation (~2200 lines)
  - `src/hardware/audio/soundblaster.cpp` - How SB uses AddSamples
  - `src/hardware/audio/opl.cpp` - How OPL uses AddSamples

## Success Criteria

- [ ] Audio devices call AddSamples() not Render()
- [ ] Resampling happens inside AddSamples
- [ ] Code structure mirrors DOSBox-Staging
- [ ] No audio glitches
- [ ] All tests pass
- [ ] LinearUpsampler marked obsolete
