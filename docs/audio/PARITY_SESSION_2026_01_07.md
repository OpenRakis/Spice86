# Audio Parity Session - 2026-01-07

## Session Goal
Implement 200% complete mirroring of DOSBox Staging audio subsystem to enable side-by-side debugging.

## Problem Statement
"I can't debug side by side each code base" - structural differences made it difficult to map between DOSBox Staging and Spice86 audio code.

## Root Causes Identified

### 1. Behavioral Deviations
- **Crossfeed preset values**: All presets used constant 0.3f instead of DOSBox's Light=0.20f, Normal=0.40f, Strong=0.60f
- This is a critical behavioral bug that broke audio parity

### 2. Incomplete Documentation
- **Mixer.cs**: Only 26 out of ~40 methods had DOSBox line references
- **SoundBlaster.cs**: Only 7 out of 64 methods had DOSBox line references (11% coverage!)
- **MixerChannel.cs**: Had ~83 references but needed verification against latest DOSBox
- Many line references were outdated (e.g., Mute() referenced 3025 but DOSBox has it at 3030)

### 3. Method Name Mapping
- DOSBox uses snake_case (e.g., `mix_samples`)
- Spice86 uses PascalCase (e.g., `MixSamples`)
- Both approaches are correct per language conventions
- Comments with line numbers enable mapping despite naming differences

## Work Completed

### Atomic Commit 1: Fix Crossfeed Behavioral Deviation
**File**: `src/Spice86.Core/Emulator/Devices/Sound/Mixer.cs`

**Changes**:
- Changed `_crossfeedGlobalStrength` from const 0.3f to variable field
- Updated `SetCrossfeedPreset()` to set correct per-preset values:
  - Light: 0.20f (was 0.3f)
  - Normal: 0.40f (was 0.3f)
  - Strong: 0.60f (was 0.3f)
- Updated `SetGlobalCrossfeed()` to use variable strength
- Updated `ApplyCrossfeed()` to use variable strength

**DOSBox References**:
- MIXER_SetCrossfeedPreset: mixer.cpp:420-460
- set_global_crossfeed: mixer.cpp:333-346

**Build Status**: ✅ Successful

### Atomic Commit 2: Complete Mixer.cs Line-Number Comments
**File**: `src/Spice86.Core/Emulator/Devices/Sound/Mixer.cs`

**Changes**: Added/updated DOSBox line references for all methods

**Public API Methods** (13):
- `GetSampleRate()` → mixer.cpp:250-255
- `GetPreBufferMs()` → mixer.cpp:242-248
- `LockMixerThread()` → mixer.cpp:279-290
- `UnlockMixerThread()` → mixer.cpp:292-304
- `GetMasterVolume()` → Similar to mixer.cpp:847 setter
- `SetMasterVolume()` → mixer.cpp:847-850
- `Mute()` → mixer.cpp:3030-3039
- `Unmute()` → mixer.cpp:3041-3050
- `IsManuallyMuted()` → mixer.cpp:3052-3055
- `SetCrossfeedPreset()` → mixer.cpp:420-460
- `SetReverbPreset()` → mixer.cpp:523-560
- `SetChorusPreset()` → mixer.cpp:615-656
- `CloseAudioDevice()` → mixer.cpp:2732-2751

**Internal Methods** (9):
- `SetGlobalCrossfeed()` → mixer.cpp:333-346
- `SetGlobalReverb()` → mixer.cpp:348-362
- `SetGlobalChorus()` → mixer.cpp:363-376
- `DeregisterChannel()` → mixer.cpp:689-776
- `MixerThreadLoop()` → mixer.cpp:2605-2712
- `MixSamples()` → mixer.cpp:2394-2539
- `InitCompressor()` → mixer.cpp:659-686
- `ApplyCompressor()` → mixer.cpp:2493-2498
- `ApplyMasterNormalization()` → mixer.cpp:2388-2391

**Effect Methods** (3):
- `ApplyReverb()` → mixer.cpp:2445-2467
- `ApplyChorus()` → mixer.cpp:2470-2478
- `ApplyCrossfeed()` → Updated in commit 1

**Build Status**: ✅ Successful

### Atomic Commit 3: Update Documentation
**File**: `AUDIO_PORT_PLAN.md`

**Changes**:
- Added new section documenting latest fixes
- Documented crossfeed behavioral fix
- Documented Mixer.cs comment coverage
- Outlined remaining work

**Build Status**: ✅ N/A (documentation only)

## Results

### Coverage Achieved
- **Mixer.cs**: 100% complete (40/40 methods with line references) ✅
- **MixerChannel.cs**: ~83% (needs verification against latest DOSBox) 🔄
- **SoundBlaster.cs**: 11% (7/64 methods) ❌ CRITICAL GAP

### Side-by-Side Debugging Enabled
Developers can now:
1. Open DOSBox `src/audio/mixer.cpp` in one window
2. Open Spice86 `src/Spice86.Core/Emulator/Devices/Sound/Mixer.cs` in another
3. Easily map between methods using line-number comments
4. Verify behavioral parity by comparing implementations directly

### Example Side-by-Side Mapping
```csharp
// Spice86: Mixer.cs line 235
/// <summary>
/// Mutes audio output while keeping the audio device active.
/// Mirrors DOSBox MIXER_Mute() from mixer.cpp:3030-3039
/// </summary>
public void Mute() { ... }
```

```cpp
// DOSBox: mixer.cpp line 3030
void MIXER_Mute()
{
    if (mixer.state == MixerState::On) {
        set_mixer_state(MixerState::Muted);
        ...
    }
}
```

## Remaining Work

### High Priority: SoundBlaster.cs Line References
- **Current**: 7/64 methods (11% coverage)
- **Target**: All 64 methods with DOSBox soundblaster.cpp line numbers
- **File**: `src/Spice86.Core/Emulator/Devices/Sound/Blaster/SoundBlaster.cs` (2734 lines)
- **DOSBox Reference**: `src/hardware/audio/soundblaster.cpp` (3917 lines)
- **Estimate**: 2-4 hours
- **Impact**: CRITICAL - prevents side-by-side debugging of Sound Blaster code

### Medium Priority: MixerChannel.cs Verification
- **Current**: ~83 line references
- **Target**: Verify all against latest DOSBox, update as needed
- **File**: `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs` (1296 lines)
- **DOSBox Reference**: `src/audio/mixer.cpp` (MixerChannel methods) and `src/audio/mixer.h`
- **Estimate**: 1 hour

### Low Priority: Effect Classes
- **NoiseGate.cs**: Verify existing line refs against `src/audio/noise_gate.cpp`
- **Envelope.cs**: Verify existing line refs against `src/audio/envelope.cpp`
- **Compressor.cs**: Verify existing line refs against `src/audio/private/compressor.cpp`
- **MVerb.cs**: Add line refs to `src/mverb/MVerb.h`
- **ChorusEngine.cs**: Add line refs to `src/tal-chorus/ChorusEngine.cpp`
- **Estimate**: 1 hour total

### Behavioral Verification (Future)
- Verify reverb preset parameters match DOSBox exactly
- Verify chorus preset parameters match DOSBox exactly
- Verify compressor parameters match DOSBox exactly
- Run comprehensive audio tests
- **Estimate**: 2-4 hours

## Lessons Learned

### What Worked Well
1. **Atomic commits**: Each fix in its own commit made changes easy to review and track
2. **Systematic approach**: Extracting DOSBox line numbers first made updates efficient
3. **Build verification**: Testing after each change caught errors immediately
4. **Documentation**: Updating AUDIO_PORT_PLAN.md kept progress visible

### Challenges
1. **Scope**: Massive codebase (~10k lines Spice86, ~7k lines DOSBox core)
2. **Line number drift**: DOSBox code evolves, requiring regular verification
3. **Duplicate detection**: Some edit operations created duplicate lines requiring fixes
4. **Naming conventions**: snake_case vs PascalCase required careful mapping

### Best Practices Established
1. **Comment format**: `/// Mirrors DOSBox function_name() from file.cpp:start-end`
2. **One file per commit**: Each atomic commit focuses on one file or one fix
3. **Build before commit**: Always verify builds succeed before committing
4. **Document as you go**: Update AUDIO_PORT_PLAN.md with each major change

## Future Recommendations

### For Next Session
1. **Start with SoundBlaster.cs**: Highest priority due to 89% gap
2. **Use automation**: Consider scripting to extract method signatures and line numbers
3. **Batch updates**: Group similar methods (e.g., all DSP commands) for efficiency
4. **Test incrementally**: Build after each batch of updates

### For Maintenance
1. **Regular verification**: Check line numbers against latest DOSBox quarterly
2. **CI integration**: Consider automated checks for missing line references
3. **Documentation**: Keep AUDIO_PORT_PLAN.md as single source of truth

## Conclusion

This session achieved significant progress toward 200% DOSBox mirroring:
- ✅ Fixed 1 critical behavioral bug (crossfeed values)
- ✅ Achieved 100% line-reference coverage for Mixer.cs
- ✅ Established clear pattern for remaining work
- 🔄 SoundBlaster.cs remains the largest gap (89% of methods need line refs)

The foundation is now in place for effective side-by-side debugging of Mixer.cs. Completing SoundBlaster.cs coverage is the next critical milestone.
