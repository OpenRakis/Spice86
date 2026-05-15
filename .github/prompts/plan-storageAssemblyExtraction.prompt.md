## Plan: Shrink PR via Storage Assembly Extraction

Assuming current FAT/ISO/CUE/features are functionally complete, reduce PR size and Core responsibility by splitting storage parsing/building from Shared into two dedicated assemblies, then rewire Core and tests with incremental, test-gated commits. This mirrors dosbox-staging’s parser-layer separation (format parsers independent from DOS/CD runtime handlers) while preserving Spice86’s C# architecture.

**Steps**
1. Phase 0 - Baseline and guardrails.
2. Confirm no behavior changes are included: this effort is structural extraction only, not feature work.
3. Capture baseline with targeted tests focused on FAT/floppy and ISO/CUE before first move. *blocks all later phases*
4. Run full suite once at baseline to detect pre-existing instability and avoid attributing unrelated failures to refactor work. *blocks all later phases*
5. Commit baseline checkpoint (no code changes) with recorded command outputs in PR notes. *depends on 3-4*

6. Phase 1 - Create assembly for FAT + floppy image tooling.
7. Add new project [src/Spice86.Storage.Fat/Spice86.Storage.Fat.csproj](src/Spice86.Storage.Fat/Spice86.Storage.Fat.csproj) and include it in [src/Spice86.sln](src/Spice86.sln). *depends on 5*
8. Move FAT/floppy-related files from Shared into the new assembly, preserving namespaces initially to minimize churn:
9. [src/Spice86.Shared/Emulator/Storage/FileSystem/BiosParameterBlock.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/BiosParameterBlock.cs)
10. [src/Spice86.Shared/Emulator/Storage/FileSystem/FatType.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/FatType.cs)
11. [src/Spice86.Shared/Emulator/Storage/FileSystem/FatFileSystem.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/FatFileSystem.cs)
12. [src/Spice86.Shared/Emulator/Storage/FileSystem/Fat12FileSystem.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/Fat12FileSystem.cs)
13. [src/Spice86.Shared/Emulator/Storage/FileSystem/FatDirectoryEntry.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/FatDirectoryEntry.cs)
14. [src/Spice86.Shared/Emulator/Storage/FileSystem/VirtualFloppyImage.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/VirtualFloppyImage.cs)
15. Keep ILoggerService dependency via reference to [src/Spice86.Shared/Spice86.Shared.csproj](src/Spice86.Shared/Spice86.Shared.csproj) interfaces package until a later cleanup PR. *depends on 8-14*
16. Update references in [src/Spice86.Core/Spice86.Core.csproj](src/Spice86.Core/Spice86.Core.csproj) and [tests/Spice86.Tests/Spice86.Tests.csproj](tests/Spice86.Tests/Spice86.Tests.csproj) to consume the new assembly. *depends on 7-15*
17. Remove moved files from Shared project compile set and keep Shared free of FAT/floppy implementations. *depends on 16*
18. Validation gate for Phase 1: targeted tests then full suite at phase end. *depends on 17*
19. Commit Phase 1 as one logical commit.

20. Phase 2 - Create assembly for ISO + CUE/BIN + CD image tooling.
21. Add new project [src/Spice86.Storage.Cd/Spice86.Storage.Cd.csproj](src/Spice86.Storage.Cd/Spice86.Storage.Cd.csproj) and include it in [src/Spice86.sln](src/Spice86.sln). *depends on 19*
22. Move CD parsing/building files from Shared into new assembly:
23. [src/Spice86.Shared/Emulator/Storage/CdRom/ICdRomImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/ICdRomImage.cs)
24. [src/Spice86.Shared/Emulator/Storage/CdRom/IsoImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/IsoImage.cs)
25. [src/Spice86.Shared/Emulator/Storage/CdRom/IsoVolumeDescriptor.cs](src/Spice86.Shared/Emulator/Storage/CdRom/IsoVolumeDescriptor.cs)
26. [src/Spice86.Shared/Emulator/Storage/CdRom/IsoDirectoryRecord.cs](src/Spice86.Shared/Emulator/Storage/CdRom/IsoDirectoryRecord.cs)
27. [src/Spice86.Shared/Emulator/Storage/CdRom/CueSheetParser.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueSheetParser.cs)
28. [src/Spice86.Shared/Emulator/Storage/CdRom/CueSheet.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueSheet.cs)
29. [src/Spice86.Shared/Emulator/Storage/CdRom/CueEntry.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueEntry.cs)
30. [src/Spice86.Shared/Emulator/Storage/CdRom/CueBinImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueBinImage.cs)
31. [src/Spice86.Shared/Emulator/Storage/CdRom/VirtualIsoImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/VirtualIsoImage.cs)
32. [src/Spice86.Shared/Emulator/Storage/CdRom/CdTrack.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CdTrack.cs)
33. [src/Spice86.Shared/Emulator/Storage/CdRom/CdSectorMode.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CdSectorMode.cs)
34. [src/Spice86.Shared/Emulator/Storage/CdRom/SectorFraming.cs](src/Spice86.Shared/Emulator/Storage/CdRom/SectorFraming.cs)
35. [src/Spice86.Shared/Emulator/Storage/CdRom/IDataSource.cs](src/Spice86.Shared/Emulator/Storage/CdRom/IDataSource.cs)
36. [src/Spice86.Shared/Emulator/Storage/CdRom/FileBackedDataSource.cs](src/Spice86.Shared/Emulator/Storage/CdRom/FileBackedDataSource.cs)
37. [src/Spice86.Shared/Emulator/Storage/CdRom/MemoryDataSource.cs](src/Spice86.Shared/Emulator/Storage/CdRom/MemoryDataSource.cs)
38. [src/Spice86.Shared/Emulator/Storage/CdRom/CdRomImageFactory.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CdRomImageFactory.cs)
39. Update references in Core and tests to point to CD storage assembly; remove moved files from Shared compile set. *depends on 21-38*
40. Validation gate for Phase 2: targeted CD tests then full suite at phase end. *depends on 39*
41. Commit Phase 2 as one logical commit.

42. Phase 3 - Keep Core focused on emulation/runtime orchestration.
43. Verify runtime-facing classes stay in Core and only consume abstractions from new storage assemblies:
44. [src/Spice86.Core/Emulator/Devices/CdRom/CdRomDrive.cs](src/Spice86.Core/Emulator/Devices/CdRom/CdRomDrive.cs)
45. [src/Spice86.Core/Emulator/OperatingSystem/DosDriveManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosDriveManager.cs)
46. [src/Spice86.Core/Emulator/InterruptHandlers/Mscdex/Mscdex.cs](src/Spice86.Core/Emulator/InterruptHandlers/Mscdex/Mscdex.cs)
47. Ensure no parser logic remains in Core and no reverse dependency from storage assemblies to Core. *depends on 41*
48. Commit import cleanup/usings/namespace adjustments separately to keep review focused. *depends on 47*

49. Phase 4 - Testing and PR slicing hygiene.
50. Add missing CUE/BIN parser tests discovered during analysis (especially multi-track and path-resolution edge cases) under existing test project so extraction does not ship with coverage gaps. *parallel with late Phase 2 if needed, must complete before final full suite*
51. Run targeted tests after each commit touching related feature area.
52. Run full test suite at end of each phase and once again after final cleanup.
53. Produce a PR comment summary mapping each commit to one extraction concern, so reviewers can review in sequence and future cherry-picks are easy.

**Relevant files**
- [src/Spice86.Shared/Emulator/Storage/FileSystem/BiosParameterBlock.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/BiosParameterBlock.cs) — FAT BPB parsing to move to FAT storage assembly.
- [src/Spice86.Shared/Emulator/Storage/FileSystem/FatFileSystem.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/FatFileSystem.cs) — FAT12/16/32 traversal core.
- [src/Spice86.Shared/Emulator/Storage/FileSystem/VirtualFloppyImage.cs](src/Spice86.Shared/Emulator/Storage/FileSystem/VirtualFloppyImage.cs) — host-folder to floppy image utility.
- [src/Spice86.Shared/Emulator/Storage/CdRom/CueSheetParser.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueSheetParser.cs) — CUE parser logic.
- [src/Spice86.Shared/Emulator/Storage/CdRom/CueBinImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CueBinImage.cs) — CUE/BIN image implementation.
- [src/Spice86.Shared/Emulator/Storage/CdRom/IsoImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/IsoImage.cs) — ISO reader.
- [src/Spice86.Shared/Emulator/Storage/CdRom/VirtualIsoImage.cs](src/Spice86.Shared/Emulator/Storage/CdRom/VirtualIsoImage.cs) — host-folder to ISO utility.
- [src/Spice86.Shared/Emulator/Storage/CdRom/CdRomImageFactory.cs](src/Spice86.Shared/Emulator/Storage/CdRom/CdRomImageFactory.cs) — parser dispatch/factory.
- [src/Spice86.Core/Emulator/Devices/CdRom/CdRomDrive.cs](src/Spice86.Core/Emulator/Devices/CdRom/CdRomDrive.cs) — runtime drive logic staying in Core.
- [src/Spice86.Core/Emulator/OperatingSystem/DosDriveManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosDriveManager.cs) — DOS drive manager staying in Core.
- [src/Spice86.Core/Emulator/InterruptHandlers/Mscdex/Mscdex.cs](src/Spice86.Core/Emulator/InterruptHandlers/Mscdex/Mscdex.cs) — INT 2Fh MSCDEX handler staying in Core.
- [src/Spice86.Core/Spice86.Core.csproj](src/Spice86.Core/Spice86.Core.csproj) — project references update.
- [src/Spice86.Shared/Spice86.Shared.csproj](src/Spice86.Shared/Spice86.Shared.csproj) — remove moved compile items and keep interface surface.
- [tests/Spice86.Tests/Spice86.Tests.csproj](tests/Spice86.Tests/Spice86.Tests.csproj) — reference both new storage assemblies.
- [tests/Spice86.Tests/Dos/FileSystem/FatFileSystemTests.cs](tests/Spice86.Tests/Dos/FileSystem/FatFileSystemTests.cs) — FAT regression safety.
- [tests/Spice86.Tests/Dos/FileSystem/VirtualFloppyImageTests.cs](tests/Spice86.Tests/Dos/FileSystem/VirtualFloppyImageTests.cs) — floppy image builder safety.
- [tests/Spice86.Tests/Emulator/Devices/CdRom/VirtualIsoImageTests.cs](tests/Spice86.Tests/Emulator/Devices/CdRom/VirtualIsoImageTests.cs) — ISO builder safety.
- [tests/Spice86.Tests/CdRom/CdRomDriveDiscSwapTests.cs](tests/Spice86.Tests/CdRom/CdRomDriveDiscSwapTests.cs) — runtime CD integration safety.
- [dosbox-staging/src/dos/drive_fat.cpp](dosbox-staging/src/dos/drive_fat.cpp) — reference architecture for parser/runtime split.
- [dosbox-staging/src/dos/drive_iso.cpp](dosbox-staging/src/dos/drive_iso.cpp) — ISO parsing boundaries.
- [dosbox-staging/src/dos/cdrom_image.cpp](dosbox-staging/src/dos/cdrom_image.cpp) — CUE/BIN parsing and format-dispatch pattern.

**Verification**
1. Baseline gate: run targeted FAT/floppy tests and targeted ISO/CUE/CD tests before first move.
2. After every commit in Phase 1: run FAT/floppy-targeted tests.
3. End of Phase 1: run full suite from [src](src).
4. After every commit in Phase 2: run CD/ISO/CUE-targeted tests.
5. End of Phase 2: run full suite from [src](src).
6. End of Phase 3 and Phase 4: run full suite again and ensure no dependency cycle or Core parser leakage remains.

**Decisions**
- Use two new assemblies: one for FAT/floppy storage and one for ISO/CUE/BIN storage.
- Keep work on current branch as logical commits (no separate prep PRs now).
- Use targeted tests per step and full suite at the end of each phase.
- Preserve Core ownership of DOS/CD runtime orchestration; extract only parsing/image-build logic.
- Scope includes host-folder image builders already in code; scope excludes implementing new storage formats beyond current branch behavior.

**Further Considerations**
1. Naming recommendation: choose [src/Spice86.Storage.Fat](src/Spice86.Storage.Fat) and [src/Spice86.Storage.Cd](src/Spice86.Storage.Cd) now to avoid churn later if NuGet packaging is desired.
2. Keep namespace stability during move first, then optionally do namespace cleanup in a final isolated commit to reduce review noise.
3. Add at least one dedicated CUE/BIN parser edge-case test before final phase gate, because current coverage is weaker than FAT/ISO coverage.
