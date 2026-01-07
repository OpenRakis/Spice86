# Audio Architecture Alignment - Project Documentation

## Overview

This directory contains comprehensive documentation for aligning Spice86's audio subsystem architecture with DOSBox-Staging for maximum emulation accuracy.

## Quick Start

**If you want to:**
- Understand the issue: Read `audio_alignment_summary.md`
- Implement the fix: Read `audio_port_plan.md` 
- Track progress: Use `audio_implementation_checklist.md`

## Critical Issue

Spice86's audio architecture currently resamples audio BEFORE passing it to the mixer, but DOSBox-Staging resamples INSIDE the mixer's AddSamples methods. This architectural difference must be corrected.

### Current (INCORRECT):
```
Audio Device → LinearUpsampler.Resample() → SoundChannel.Render() → Mixer
```

### Required (CORRECT - matches DOSBox-Staging):
```
Audio Device → MixerChannel.AddSamples() [Convert + Resample + Filter] → audio_frames
```

## Documentation Files

### 1. audio_alignment_summary.md
**Quick reference guide**
- Problem statement
- Solution overview
- Files to modify
- Success criteria
- ~5 minute read

### 2. audio_port_plan.md  
**Complete implementation guide**
- 9 implementation phases
- Detailed step-by-step instructions
- Code examples for each change
- Critical implementation notes
- Testing strategy
- ~20 minute read

### 3. audio_implementation_checklist.md
**Detailed task tracking**
- 91 granular tasks across 9 phases
- Checkbox format for easy tracking
- Test requirements for each phase
- Status tracking fields
- Use this to track implementation progress

## Current Status

**Phase 1 (Infrastructure): ✅ COMPLETE**
- Analysis complete
- Documentation complete
- Basic types created
- Ready for implementation

**Phase 2-9: 📋 DOCUMENTED, AWAITING IMPLEMENTATION**

## Implementation Order

Follow this sequence:

1. **Phase 2:** Create MixerChannel class with AddSamples methods
2. **Phase 3:** Update SoftwareMixer (remove resampling from Render)
3. **Phase 4:** Update Sound Blaster (call AddSamples instead of Render)
4. **Phase 5:** Verify OPL3 uses AddSamples correctly
5. **Phase 6:** Update other audio devices
6. **Phase 7:** Integration and regression testing
7. **Phase 8:** Advanced features (optional)
8. **Phase 9:** Cleanup and final documentation

## Key Principles

### Atomic Commits
Each commit should:
- Represent one logical change
- Be compilable
- Be testable
- Have a clear description

### Testing
After each phase:
- Run unit tests
- Run integration tests
- Test with real DOS programs
- Verify no audio glitches

### Mirroring DOSBox-Staging
The goal is 200% architectural alignment:
- Same method names where possible
- Same flow structure
- Same resampling location
- Same sample conversion logic

## Code Structure Reference

### DOSBox-Staging Source (for reference)
Located in: `/tmp/dosbox-staging/` (cloned during analysis)

Key files to reference:
- `src/audio/mixer.h` - MixerChannel interface
- `src/audio/mixer.cpp` - AddSamples implementation (lines 2125-2268)
- `src/hardware/audio/soundblaster.cpp` - SB device usage
- `src/hardware/audio/opl.cpp` - OPL device usage

### Spice86 Files to Modify

**New:**
- `src/Spice86.Core/Emulator/Devices/Sound/MixerChannel.cs`

**Modified:**
- `src/Spice86.Core/Emulator/Devices/Sound/SoftwareMixer.cs`
- `src/Spice86.Core/Emulator/Devices/Sound/Blaster/SoundBlaster.cs`
- `src/Spice86.Core/Emulator/Devices/Sound/Opl3Fm.cs`

**Deprecated:**
- `src/Spice86.Core/Emulator/Devices/Sound/Blaster/LinearUpsampler.cs`

## Resources

### External References
- DOSBox-Staging repository: https://github.com/dosbox-staging/dosbox-staging
- Speex resampler: https://www.speex.org/

### Internal References
- Spice86 architecture: `../cfgcpuReadme.md`
- Copilot instructions: `../.github/copilot-instructions.md`

## Success Criteria

Implementation is complete when:
- ✅ All 91 checklist tasks completed
- ✅ All audio devices call AddSamples (not Render)
- ✅ Resampling happens inside AddSamples
- ✅ Code structure mirrors DOSBox-Staging
- ✅ All tests pass
- ✅ No audio glitches in test games
- ✅ LinearUpsampler marked obsolete
- ✅ Documentation updated

## Getting Help

If you encounter issues:
1. Review the detailed implementation guide in `audio_port_plan.md`
2. Check DOSBox-Staging source code for reference
3. Run tests frequently to catch issues early
4. Make small atomic commits

## License

This documentation is part of the Spice86 project and follows the same license.

---

**Last Updated:** 2026-01-07  
**Status:** Analysis Complete, Ready for Implementation  
**Next Action:** Begin Phase 2 - Create MixerChannel class
