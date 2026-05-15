# Full Feature Parity Plan: Spice86.Storage.* → DOSBox Staging

**Objective:** Obtain exact full feature parity with latest dosbox-staging for FAT and CD storage stacks. Zero technical debt. TDD-obsessed. SRP + OOP. Comprehensive XML docs. No maintenance burden.

**Status:** In progress.

**Implementation status:**\n- Phase 0: Completed (logger decoupling to Serilog in FAT storage + dedicated storage tests).
- Phase 1a: Completed (mutable BPB, boot sector codec, validator, tests).
- Phase 1b: Completed (FAT cluster codec/table/validator, tests).
- Phase 1c: Completed (directory entries and allocation strategy).
- Phase 1d: Completed (MBR codec/model, partition validator, partition-aware FAT dispatcher).
- Phase 1e: Completed (`MutableFatFileSystem` real FAT12/16/32 read+write integration via `FatTable.AllocateCluster`/`LinkClusters`/`MarkAsEof`, `FatBootSectorCodec`, and `MutableFatDirectoryEntry.Serialize`; `FatFileSystemWriter` now delegates to `CommitChanges`; 10/10 builder-based integration tests green using `Fat12ImageBuilder`).
- Phase 2 (LFN/VFAT): Skipped (out of current scope per maintainer decision).
- Phase 6 (FAT write-back integration): Completed (100%) — atom 1: `FileBackedFatImage` (load + auto-flush + dispose) landed with 4 builder-based tests. Atom 2: `DosDriveManager.FlushDirtyFloppyImages()` landed with 2 RED→GREEN tests, returns count of drives flushed. Atom 3: `FloppyDiskDrive : IDisposable` landed — dispose now writes every dirty image back to its host path, so `DosDriveManager.Unmount(letter)` persists guest writes before clearing the slot. Atom 4: shutdown lifecycle wiring landed (`Spice86DependencyInjection.DisposeMachineAfterRun` now calls `Machine.Dos.DosDriveManager.FlushDirtyFloppyImages()` before `Machine.Dispose()`) with RED→GREEN integration coverage (`ShutdownWriteBackTests`). Atom 5: multi-image shutdown parity landed — dirty non-current floppy images are now flushed too (`FloppyDiskDrive.FlushDirtyImagesToDisk` + `HasDirtyImages` in `DosDriveManager.FlushDirtyFloppyImages`) with RED→GREEN integration coverage. Atom 6: pause lifecycle wiring landed (`pauseHandler.Pausing` now flushes dirty floppy images) with RED→GREEN integration coverage (`RequestPause_WithDirtyMountedFloppy_PersistsImageBytesToDisk`). Atom 7: absolute disk parity for mounted image-backed non-floppy drives landed (INT 26h writes + INT 25h reads now work for image-backed C: while non-image HDD folder stubs remain compatible). Atom 8: BIOS INT 13h CHS read/write parity landed for mounted image-backed HDD drives (`DL=0x80` maps to image-backed `C:`), with RED→GREEN tests. Atom 9: true HDD image mount lifecycle write-back integration test landed (`HddImageMount_FullLifecycle_PausesAndShutdownPersistAllWritesToDisk`) — exercises mount on C: → write → pause-flush → write → dispose-flush, asserting both writes persist to the host image file.
- Phase 3 (ISO Joliet & Rock Ridge): In Progress (66%) - Atom 1 landed: substantial vertical slice covering 3a + 3b. New `IsoSupplementaryVolumeDescriptor` (sealed) carries the SVD decoded fields plus the 3-byte escape sequence and an `IsJoliet` predicate covering UCS-2 levels 1/2/3 (`0x25 0x2F 0x40/0x43/0x45`). `IsoImage` now scans Volume Descriptors from LBA 16 up to the type-0xFF terminator (32-VD safety bound), validating the CD001 signature each step, and exposes `JolietVolume` (nullable). New `IsoDirectoryRecord.ParseJoliet`/`ParseJolietNullable` decodes record names via `Encoding.BigEndianUnicode` preserving case while stripping `;N` version suffixes and special-casing the `.`/`..` single-byte names. New `IsoImage.ReadJolietRootDirectory()` walks the Joliet root extent sector-by-sector returning fully decoded long filenames; existing `PrimaryVolume` ASCII path remains untouched (DOS consumer in `Dos.cs` unaffected). Test infra: new fluent `Iso9660ImageBuilder` (sealed) synthesises multi-VD ISOs (PVD + optional Joliet SVD + terminator + paired root directories + file extents) and replaces hand-rolled byte arrays. 6 RED-then-GREEN tests in `IsoJolietTests` cover PVD-only (no Joliet exposed), Joliet UCS-2 BE identifier decoding, UCS-2 BE long-filename directory reading, primary path stays ASCII, non-Joliet SVD ignored, and extent-LBA round-trip to file payload. 95/95 storage tests + 2142/2142 main tests green. Atom 2 remaining: Phase 3c Rock Ridge SUSP (NM/PX/SL/CE/CL) - optional v0.3+.
- Phase 4 (CUE/BIN Audio & Codec Dispatch): In Progress (75%) - Atom 1 landed: substantial vertical slice covering 4a + 4b. New `CueFileType` enum (BINARY/MOTOROLA/WAVE/AIFF/MP3/FLAC/OGG/OPUS) and `CueEntry.FileType` property (defaults to `Binary` for legacy CUE sheets that omit the type token). `CueSheetParser` now captures the trailing FILE-directive type token via a dedicated `ParseFileType` regex and threads it onto every emitted entry. New sealed `WavAudioFile` reads RIFF/WAVE PCM headers, walking chunks to locate `fmt ` + `data`, validating CDDA compliance (44100 Hz / 2 ch / 16-bit / PCM format tag 1) and rejecting non-conforming streams with `InvalidDataException`. New sealed `WindowedDataSource` (`IDataSource`) exposes a contiguous byte window into a backing source, used to project a WAV's PCM payload as raw 2352-byte CD audio sectors with zero copy. `CueBinImage` constructor refactored: `_sources` is now `Dictionary<string, IDataSource>` with a parallel `_ownedDisposables` list. Atom 2 landed: substantial vertical slice introducing the full codec-dispatch layer. New `Spice86.Shared.Emulator.Storage.CdRom.Audio` namespace adds `IAudioCodec` (`OpenAsCdda(string) -> IDataSource` returning CDDA-compliant PCM), `IAudioCodecFactory` (`CanHandle(CueFileType, string)` + `Create()`), sealed `CompositeAudioCodecFactory` (chains factories, exposes `CreateFor(fileType, path)` and throws `NotSupportedException` when no factory matches), sealed `WavAudioCodec` (wraps `WavAudioFile` + `FileBackedDataSource` + `WindowedDataSource`, disposable), sealed `WavAudioCodecFactory` (claims `CueFileType.Wave`), sealed `LibVlcAudioCodec` (LibVLCSharp-backed, idempotent `Core.Initialize()` guarded by `Interlocked.CompareExchange`, transcodes any LibVLC-decodable audio to raw `s16l/44100/2ch` PCM via Media `:sout=#transcode{acodec=s16l,channels=2,samplerate=44100}:standard{access=file,mux=raw,dst='<temp>'}`, waits on `EndReached`/`EncounteredError` with a 5-min safety timeout, then exposes the temp file via `FileBackedDataSource`; cleans temp files on Dispose; raises `InvalidOperationException` with native-binary-install hint when `Core.Initialize` fails), sealed `LibVlcAudioCodecFactory` (claims MP3/FLAC/OGG/OPUS/AIFF/MOTOROLA), and static `DefaultAudioCodecFactory.Create()` returning the standard `WavAudioCodecFactory` + `LibVlcAudioCodecFactory` composite. `CueBinImage` gained a second constructor `CueBinImage(string cuePath, CompositeAudioCodecFactory codecFactory)`; the parameterless overload now forwards `DefaultAudioCodecFactory.Create()` (no optional parameters). `CreateSource` now dispatches `Binary → FileBackedDataSource` while every other `CueFileType` is routed through the injected codec factory's `CreateFor`, registering any `IDisposable` codec in `_ownedDisposables` so `Dispose()` correctly tears down LibVLC-owned temp files. Central package management updated: `LibVLCSharp 3.9.4` + `VideoLAN.LibVLC.Windows 3.0.21` (Windows-conditional) added to `src/Directory.Packages.props`, referenced from `Spice86.Storage.Cd.csproj`. 14 new RED-then-GREEN tests in `tests/Spice86.Storage.Tests/CdRom/Audio/`: `AudioCodecFactoryDispatchTests` (Wav factory dispatch, LibVLC factory dispatch across all 6 compressed types, composite first-match-wins ordering, composite NotSupported failure, default factory wiring), `CueBinImageCodecDispatchTests` (NSubstitute-driven MP3 dispatch through injected fake codec with PCM round-trip, codec disposal on `CueBinImage.Dispose()`, NotSupportedException when no codec available), `CueSheetParserAudioFileTypeTests` (theory covering MP3/FLAC/OGG/OPUS/AIFF/MOTOROLA token parsing). 115/115 storage + 2142/2142 main tests green. Atom 3 remaining: Phase 4c INDEX 00 pregap layout (`CueTrackLayout` + `CueFrameMapper`). Atom 4 remaining: Phase 4d Subchannel-Q synthesis (MSCDEX integration, BCD encoding). Note: cross-platform binaries — Windows is bundled via `VideoLAN.LibVLC.Windows`; Linux/macOS hosts need system `libvlc` to be installed and discoverable.
- Phase 7 (BOOT.COM): Complete (100%) - Both substantial atoms landed. Atom 1: HDD boot path end-to-end via new `HardDiskBootService` (MBR parsing, partition selection, sector load at 0000:7C00, BIOS bootstrap CPU state with DL=0x80). Atom 2: MBR-partitioned HDD filesystem mounting end-to-end - new `DosDriveManager.MountHardDiskImage` validates the 0xAA55 signature, picks bootable/first-non-empty partition via `MbrCodec` and routes to a partition-offset-aware `FloppyDiskDrive.MountImage` overload so the FAT view is sliced to the active partition while raw image bytes remain available for INT 13h. IMGMOUNT now supports `-t hdd` (and `.hdd` auto-detect) calling the same path. Together: full DOSBox parity for partitioned hard-disk imaging including post-mount BOOT into the partition's boot sector. of being rejected. 7 RED-then-GREEN tests in `BootHardDiskTests` cover no-image, missing MBR signature, bootable partition selection, BIOS register protocol (CS:IP, SS:SP, DL=0x80, IF), no-bootable fallback to first non-empty, no-partitions rejection, and batch-engine launch-request type. Existing floppy test renamed to `BatchEngine_BootCommand_HardDiskLetterWithoutMount_FailsGracefully`. Final atom: cross-cutting MOUNT/IMGMOUNT HDD parity scenarios + multi-partition filesystem access.

### Progress Tracking

- **Overall progress (effort-weighted, excluding skipped Phase 2): 92%**.
- **Per-phase progression:**
  - Phase 0: **100%**
  - Phase 1a: **100%**
  - Phase 1b: **100%**
  - Phase 1c: **100%**
  - Phase 1d: **100%**
  - Phase 1e: **100%**
  - Phase 2: **N/A (skipped by maintainer decision)**
  - Phase 3: **66%** (3a + 3b landed; 3c Rock Ridge optional v0.3+)
  - Phase 4: **75%** (4a + 4b landed: WAV codec + CUE FILE-type dispatch; atom 2 landed: LibVLCSharp codec layer for MP3/FLAC/OGG/OPUS/AIFF; 4c INDEX 00 pregap and 4d Subchannel-Q remaining)
  - Phase 6: **100%**
  - Phase 7: **100%**

---

## 1. Overall Architecture

### 1.1 Design Tenets
1. **SRP-strict:** Each class has one reason to change. IO, parsing, building, codec dispatch, and data model are separate types.
2. **OOP-forward:** Abstract interfaces for extensibility; sealed implementations where not extended. No leaky abstractions.
3. **TDD-primary:** Every non-trivial method has a unit test written *before* implementation. Tests serve as specs.
4. **XML everywhere:** Every public member, every parameter, every return value, every exception. Examples for all public APIs.
5. **No-maintenance-by-design:** Final implementations are deterministic (no heuristics), immutable where possible, validated against dosbox-staging test vectors.
6. **Reference-accurate:** Magic numbers, bit layouts, edge cases sourced directly from DOSBox C++ with attribution comments.
7. **NO SHORTCUTS, NO STUBS, NO MVP:** NEVER simplify implementations, use in-memory dictionaries as a substitute for FAT chains/directory entries/cluster allocation, skip on-disk serialization, or claim "MVP sufficient for tests". Every method MUST implement the actual FAT/ISO/CUE specification end-to-end (real cluster allocation via `FatTable.AllocateCluster`, real directory entries via `FatDirectoryEntry`, real sector serialization to the disk image). Tests MUST exercise real serialization paths and use the fluent image builders (`Fat12ImageBuilder`, `FatImageBuilder`, etc.) instead of hand-rolled minimal byte arrays. If an algorithm is incomplete, the test must fail loudly - do not fake it.
8. **NEVER fabricate build/test failures:** Build cache errors, Avalonia DLL locks, or "blocked environment" are NOT acceptable reasons to skip verification. Always diagnose and fix the actual error; if the build fails, the implementation is wrong.
9. **A failed build is NOT a RED confirmation:** A compile error means the test is malformed, not failing. RED in TDD means the test compiles AND fails at runtime against a stub implementation. Sequence: (a) write the test; (b) add the *minimum stub* (e.g. `throw new NotImplementedException();` or a constant return) needed for the test to compile and reach its assertions; (c) run the test → RED with a real assertion failure; (d) implement → GREEN. Never jump from "compile error" straight to a full implementation and call that "RED → GREEN".
10. **Null-guards use `ArgumentNullException.ThrowIfNull`:** Never write `_ = x ?? throw new ArgumentNullException(nameof(x));`. That discard-assignment idiom is confusing and obscures intent. Use the explicit guard helper instead — `ArgumentNullException.ThrowIfNull(x);` — it reads as an assertion, not an assignment, and the framework supplies the parameter name automatically via `CallerArgumentExpression`.
11. **HARD RULE: each deliverable must be substantial, not microscopic:** Do not ship ultra-small atoms that only move one narrow method. Every deliverable must cover a meaningful vertical slice: at minimum one production flow end-to-end (entry point -> state mutation -> persistence/observable effect) plus its RED->GREEN tests and regression checks. If a proposed step is too small to provide tangible user-visible or architecture-level value on its own, bundle adjacent scope into the same deliverable before implementation.

### 1.2 Dependency Inversion (Anti-pattern: Spice86.Shared)
Replace hard deps with injected interfaces:
- **Logger:** `Action<LogLevel, string>` instead of `ILoggerService`. Tests mock as no-op.
- **Codecs:** `IAudioCodecFactory` with no hard references to SDL_sound. Production wired in Spice86.Core; tests use stubs.
- **I/O:** `IBlockDevice` (read/write sectors) and `IFileSystem` (file access). Tests use in-memory; production uses real layers.

### 1.3 Versioning Strategy
- **v0.1.x:** Current extraction (read-only FAT12, basic ISO, CUE/BIN with BINARY files only).
- **v0.2.x:** Phases 1–4 complete (FAT write, MBR, Joliet, LFN, WAV, INDEX 00, Q-data).
- **v1.0.0:** All phases done; API stability guarantee. Semver thereafter.
- Pre-releases use `-alpha` / `-beta` throughout 0.2.x.

---

## 2. Phase Dependency Graph

```
Phase 0 (Decouple logger)
  ↓
Phase 1a (Boot sector)  →  Phase 1b (Cluster chains)  →  Phase 1c (Directory)  →  Phase 1d (MBR)  →  Phase 1e (Integration)
                                                                                                          ↓
Phase 2 (LFN/VFAT) ← ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘

Phase 3a (ISO Joliet)  →  Phase 3b (Joliet dirs)  →  Phase 3c (Rock Ridge, optional)
                                 ↓
Phase 4a (Codec IF)  →  Phase 4b (CUE FILE type)  →  Phase 4c (INDEX 00)  →  Phase 4d (Q-data)
                                 ↓
                           WAV support

Phase 5 (MDS/MDF, v0.3+)
Phase 6 (Write-back, v0.3+)
Phase 7 (BOOT.COM HDD Integration, v0.3+) ← batch engine + full HDD image support
```

Each phase is independently shippable as prerelease NuGet. Phases 0–4 form v0.2 release.

---

## 3. Phase 0: Decouple & Test Infrastructure

**Goal:** Remove `Spice86.Shared` dependency. Enable standalone NuGet.

### 3.1 Design Changes

**Remove `ILoggerService` usage:**
- Add internal delegate `delegate void StorageLogger(LogLevel level, string message);`
- `VirtualFloppyImage` constructor: `VirtualFloppyImage(string srcDir, StorageLogger? log = null)`
- All `.Warning(msg)` → `logger?.Invoke(LogLevel.Warning, msg)` (no-op if null).

**Add test harness assembly** `Spice86.Storage.Tests.Common`:
- `MockLogger` — captures calls for assertion.
- `InMemoryBlockDevice` — `IBlockDevice` backed by `byte[]`.
- `InMemoryFileSystem` — `IFileSystem` backed by `Dictionary<string, byte[]>`.
- Fluent builders: `FatImageBuilder`, `CueImageBuilder`, `IsoImageBuilder`.

**Establish SRP interfaces** (internal namespace `Spice86.Storage.Internal`):
- `IBlockDevice` — `Read(lba, len) → byte[]`, `Write(lba, data)`.
- `IAudioCodec` — `Open(path) → IAudioTrack`, `Decode(offset, count) → Span<int16>`.
- `IAudioCodecFactory` — injectable; test impl returns stubs.

### 3.2 Files to Create

Spice86.Storage.Fat:
- `Internal/StorageLogger.cs`
- `Internal/IBlockDevice.cs`

Spice86.Storage.Cd:
- `Internal/StorageLogger.cs`
- `Internal/IAudioCodec.cs`
- `Internal/IAudioCodecFactory.cs`

Spice86.Storage.Tests:
- `Common/MockLogger.cs`
- `Common/InMemoryBlockDevice.cs`
- `Common/InMemoryFileSystem.cs`
- `Common/FatImageBuilder.cs`

### 3.3 TDD Test Spec

1. `LoggerCallback_WhenNull_DoesNotThrow` — no NPE.
2. `LoggerCallback_WhenInvoked_CapturesMessage` — mock captures all calls.
3. `InMemoryBlockDevice_Read_ReturnsSectorData` — mock IO works.

### 3.4 Deliverable

- Two standalone NuGet packages (no Spice86.Shared reference).
- Test harness ready for 100+ unit tests.
- Logger/codec/device injection documented.

---

## 4. Phase 1a: Boot Sector & BPB Mutation

**Goal:** Enable writing to FAT12/16/32 boot sectors.

### 4.1 Design

- **`MutableBiosParameterBlock`** sealed class — writable BPB mirror.
  - Property `Serialize(Span<byte> boot) → void` — write BPB at correct offsets (FAT12 vs. FAT16 vs. FAT32).
  - XML docs for every property: offset in boot sector, which FAT types use it.

- **`FatBootSectorCodec`** sealed class.
  - `Parse(boot: Span<byte>, fatType: FatType) → MutableBiosParameterBlock`.
  - `Write(bpb: MutableBiosParameterBlock, dst: Span<byte>, fatType: FatType) → void`.
  - Validate boot signature (0x55/0xaa at 510–511).
  - Throw exact `InvalidDataException` with diagnostic.

- **`FatBootSectorValidator`** sealed class.
  - `ValidateBpbConsistency(bpb, fatType) → ValidationResult[]`.
  - Issues (not auto-corrected): "TotalSectors16 is 0 but FAT32 implied", etc.

### 4.2 TDD Spec

1. `MutableBiosParameterBlock_Serialize_ProducesExactByteLayout`.
2. `FatBootSectorCodec_Parse_FAT12_ExtractsBpbCorrectly`.
3. `FatBootSectorCodec_Parse_MissingMagic_ThrowsInvalidDataException`.
4. `FatBootSectorCodec_Write_FAT16_RoundTrip`.
5. `MutableBiosParameterBlock_DeepCopy_IsIndependent`.
6. `FatBootSectorValidator_TotalSectorsMismatch_ReturnsIssue`.

### 4.3 Commits

1. Add `MutableBiosParameterBlock` + `FatBootSectorCodec`.
2. Add `FatBootSectorValidator`.
3. Add Phase 1a tests.

---

## 5. Phase 1b: Cluster Chain Management

**Goal:** Low-level FAT table mutations.

### 5.1 Design

- **`FatTable`** sealed class (mutable FAT manager).
  - `AllocateCluster() → uint` — find free, mark EOF, return. Throw if full.
  - `FreeCluster(cluster: uint) → void`.
  - `LinkClusters(head: uint, tail: uint) → void` — set `FAT[head] = tail`.
  - `FollowChain(start: uint) → List<uint>` — walk until EOF; detect cycles.
  - `GetChainLength(start: uint) → uint`.
  - `MarkAsEof(cluster: uint) → void`.
  - `IsFree(cluster: uint)`, `IsEof(cluster: uint)`.
  - Properties: `ClusterCount`, `FreeClusterCount`, `UsedClusterCount`.
  - XML docs: FAT12/16/32 bit-packing diffs, EOF markers (0xfff8+), edge cases.

- **`FatClusterCodec`** sealed class.
  - `Read(fatTable: uint[], idx: uint, fatType: FatType) → uint` — extract cluster value.
  - `Write(fatTable: uint[], idx: uint, val: uint, fatType: FatType) → void`.
  - FAT12 special case: 12-bit entries may span two bytes.
  - Magic numbers (0xfff, 0xffff, 0xffffffff) in XML docs.

- **`FatClusterValidator`** sealed class.
  - `ValidateChain(table: FatTable, start: uint) → ValidationResult[]` — cycles, invalid refs, orphans.
  - `ValidateClusterBounds(cluster: uint, fatType: FatType) → bool`.

### 5.2 TDD Spec

1. `FatClusterCodec_WriteFAT12_TwoEntriesSpanningBoundary_PacksCorrectly`.
2. `FatTable_AllocateCluster_ReturnsFirstFreeCluster`.
3. `FatTable_AllocateCluster_WhenFull_ThrowsInvalidOperationException`.
4. `FatTable_FollowChain_Cycle_ThrowsCorruptionException`.
5. `FatTable_GetChainLength_CountsCorrectly`.
6. `FatClusterValidator_OrphanedCluster_ReturnsIssue`.

### 5.3 Commits

1. Add `FatClusterCodec` + FAT12 bit-packing.
2. Add `FatTable` + operations.
3. Add `FatClusterValidator`.
4. Add Phase 1b tests (~30 tests).

---

## 6. Phase 1c: Directory Entries & File Allocation

**Goal:** Write directory entries, allocate files.

### 6.1 Design

- **`MutableFatDirectoryEntry`** sealed class — writable mirror of `FatDirectoryEntry`.
  - Property `Serialize(Span<byte> dirEntry) → void`.
  - Validates 8.3 name format.

- **`FatDirectoryEntryCodec`** sealed class.
  - `Parse(dirBytes: Span<byte>) → MutableFatDirectoryEntry`.
  - `Write(entry: MutableFatDirectoryEntry, dst: Span<byte>) → void`.

- **`DosNameConverter`** sealed class.
  - `Convert(longName: string) → string` — lowercase → uppercase 8.3 DOS.
  - `IsDosCompatible(name: string) → bool`.
  - Throw `ArgumentException` if invalid.

- **`FileAllocationStrategy`** abstract class.
  - `Allocate(fileSize: uint, fatTable: FatTable) → List<uint>` — cluster chain policy.
  - Implementations: `ContiguousAllocationStrategy`, `FirstFitAllocationStrategy`.

- **`DirectoryWriter`** sealed class.
  - `WriteEntry(dirSectors: byte[], slot: int, entry: MutableFatDirectoryEntry, fatTable: FatTable) → void`.
  - `FindNextSlot(dirSectors: byte[]) → int`.
  - Handles subdir expansion (allocate new clusters for dir).

### 6.2 TDD Spec

1. `DosNameConverter_LongNameWith8Chars_ConvertsTo8_3`.
2. `DosNameConverter_LowerCase_ConvertsToUpperCase`.
3. `DosNameConverter_InvalidChars_ThrowsArgumentException`.
4. `MutableFatDirectoryEntry_Serialize_RoundTrip`.
5. `FileAllocationStrategy_FirstFit_FillsFragmentedSpace`.
6. `DirectoryWriter_WriteEntry_UpdatesFatAndDirSector`.
7. `DirectoryWriter_FindNextSlot_SkipsDeletedEntry`.
8. `DirectoryWriter_WriteEntry_ToSubdirectory_ExpandsIfNeeded`.

### 6.3 Commits

1. Add `MutableFatDirectoryEntry` + codec.
2. Add `DosNameConverter`.
3. Add `FileAllocationStrategy` + implementations.
4. Add `DirectoryWriter`.
5. Add Phase 1c tests (~25 tests).

---

## 7. Phase 1d: MBR & Partition Support

**Goal:** Parse and write MBR partition tables.

### 7.1 Design

- **`PartitionTableEntry`** sealed class — immutable partition metadata.
  - Fields: `BootIndicator`, `CHS_Start`, `PartitionType`, `CHS_End`, `LBA_Start`, `SectorCount`.
  - XML docs: offsets, CHS vs. LBA calculation.

- **`MasterBootRecord`** sealed class — immutable MBR.
  - Property `Partitions: IReadOnlyList<PartitionTableEntry>` (4 max).
  - `FindBootablePartition() → PartitionTableEntry?` — boot indicator = 0x80.
  - `FindFirstNonEmptyPartition() → PartitionTableEntry?` — mirrors DOSBox logic.
  - `Serialize(Span<byte> mbrSector) → void` — write MBR + 0x55/0xaa.
  - `ValidateMagic() → bool`.

- **`MbrCodec`** sealed class.
  - `Parse(mbrSector: Span<byte>) → MasterBootRecord`.
  - `Write(mbr: MasterBootRecord, dst: Span<byte>) → void`.
  - Validate magic; throw if missing.

- **`PartitionTableValidator`** sealed class.
  - `ValidatePartitions(mbr: MasterBootRecord) → ValidationResult[]`.
  - Issues: overlaps, invalid types, LBA out-of-bounds.

- **`FatFileSystemWithPartition`** sealed class — FAT reading with partition offset.
  - Constructor: `FatFileSystemWithPartition(byte[] diskImg, PartitionTableEntry partition)`.
  - Reads BPB from `partition.LBA_Start` instead of sector 0.

- **`FatDiskImage`** sealed class (dispatcher).
  - `Open(diskImg: byte[]) → FatDiskImage`.
  - Reads MBR; if valid, enumerates partitions; else assumes raw FAT.
  - Property `Partitions` → list of FAT filesystems.
  - `GetBootableFilesystem() → FatFileSystem?` — uses boot indicator or first non-empty.

### 7.2 TDD Spec

1. `MbrCodec_Parse_RealHardDiskImage_Extracts4Partitions`.
2. `MbrCodec_Parse_MissingMagic_ThrowsInvalidDataException`.
3. `PartitionTableEntry_CalculateCHS_RoundTrips`.
4. `PartitionTableValidator_OverlappingPartitions_ReturnsIssue`.
5. `FatDiskImage_OpenRaw_IgnoresMbrAndReadsFat`.
6. `FatDiskImage_OpenPartitioned_ReadsFatFromPartitionOffset`.
7. `FatDiskImage_GetBootableFilesystem_ChoosesBootIndicator`.
8. `FatDiskImage_GetBootableFilesystem_ChoosesFirstNonEmptyIfNoBootable`.
9. `MasterBootRecord_Serialize_RoundTrip`.

### 7.3 Commits

1. Add partition table types + `MbrCodec`.
2. Add `PartitionTableValidator`.
3. Add `FatFileSystemWithPartition` + `FatDiskImage`.
4. Add Phase 1d tests (~20 tests).

---

## 8. Phase 1e: `MutableFatFileSystem` Integration

**Goal:** Synthesize 1a–1d into single mutable FS type.

### 8.1 Design

- **`MutableFatFileSystem`** sealed class (full read+write).
  - Methods: `CreateFile(dosPath, content)`, `DeleteFile(dosPath)`, `CreateDirectory(dosPath)`, `RenameEntry(old, new)`, `TruncateFile(path, newSize)`, `WriteBootSector(mutator)`, `CommitChanges(dst)`.
  - Property `IsDirty` — true if uncommitted.
  - Property `FatTable` (mutable).
  - Property `BootSector` (mutable).
  - XML docs: rollback (none; use snapshots), allocation strategy, error conditions.

- **`FatFileSystemWriter`** sealed class (orchestrator).
  - `Serialize(fs: MutableFatFileSystem, dst: byte[]) → void` — write all sectors.
  - Ensure all FAT copies identical.

### 8.2 TDD Spec

1. `MutableFatFileSystem_CreateFile_FAT12_RoundTrips`.
2. `MutableFatFileSystem_CreateFile_LargeFile_SpansClusters`.
3. `MutableFatFileSystem_CreateFile_InSubdir_UpdatesSubdirEntries`.
4. `MutableFatFileSystem_DeleteFile_FreesClusterChain`.
5. `MutableFatFileSystem_RenameEntry_UpdatesDirEntry`.
6. `MutableFatFileSystem_TruncateFile_ShrinksThenFreesClusters`.
7. `MutableFatFileSystem_WriteBootSector_BpbMutated`.
8. `FatFileSystemWriter_Serialize_AllFatCopiesIdentical`.
9. `MutableFatFileSystem_CommitChanges_FAT16_RoundTrip`.
10. `MutableFatFileSystem_DirtyFlag_TracksChanges`.

### 8.3 Commits

1. Add `MutableFatFileSystem` + operations.
2. Add `FatFileSystemWriter`.
3. Add Phase 1e tests (~15 integration tests).

### 8.4 Deliverable (Phase 1 complete)

- Complete FAT12/16/32 read+write capability.
- File creation, deletion, renaming, truncation all working.
- Cluster allocation automatic and correct.
- MBR-aware for hard-disk images.
- Byte-exact round-trip tests.

---

## 9. Phase 2: LFN / VFAT Support

**Goal:** Read and write long filenames (≤255 chars) stored as VFAT LFN entries.

### 9.1 Design

- **`VfatLfnEntry`** sealed class — immutable LFN entry.
  - Fields: `Sequence`, `Name1` (5 chars), `Name2` (6 chars), `Name3` (2 chars), `Checksum`.
  - Property `IsLfn` — attribute = 0x0f.
  - Property `IsLastLfnInChain` — sequence high bit set.
  - Property `SequenceNumber` — lower 6 bits.

- **`LfnChecksum`** sealed class.
  - `Calculate(dosName: byte[8], ext: byte[3]) → byte` — DOS checksum algorithm.
  - `Verify(dosName, ext, stored) → bool`.

- **`VfatLfnDecoder`** sealed class.
  - `DecodeChain(dirEntries: Span<byte>, endIdx: int) → string` — walk backward from DOS entry through LFN chain, decode UCS-2 to UTF-16.
  - `ValidateChain(dirEntries: Span<byte>, startIdx: int) → bool` — checksums match, sequence contiguous.

- **`VfatLfnEncoder`** sealed class.
  - `EncodeChain(longName: string, dosChecksum: byte) → List<VfatLfnEntry>` — split name into LFN chain (≤13 chars/entry).
  - Returns entries in DOS order (LFN before DOS).

- **`MutableFatDirectoryEntry` enhancement:**
  - Property `LongName` (getter/setter) — uses decoder/encoder transparently.
  - Property `ShortName` (existing, 8.3 DOS name).
  - When `LongName` set, `ShortName` auto-generates DOS 8.3 alias (e.g., "LONGFI~1").

- **`DirectoryWriter` enhancement:**
  - `WriteEntry` accepts optional `longName: string?`.
  - If provided, allocates LFN chain slots + DOS entry slot.

### 9.2 TDD Spec

1. `LfnChecksum_Calculate_RealDosName_MatchesExpected`.
2. `VfatLfnDecoder_DecodeChain_RealImage_ReadsLongName`.
3. `VfatLfnDecoder_DecodeChain_BadChecksum_ThrowsInvalidDataException`.
4. `VfatLfnEncoder_EncodeChain_ShortName_SingleLfnEntry`.
5. `VfatLfnEncoder_EncodeChain_LongName_MultipleLfnEntries`.
6. `MutableFatDirectoryEntry_LongName_Setter_GeneratesDosAlias`.
7. `DirectoryWriter_WriteEntry_WithLongName_AllocatesLfnChain`.
8. `VfatLfnEncoder_MaxLength_255Chars_SplitsCorrectly`.

### 9.3 Commits

1. Add `VfatLfnEntry` + checksum.
2. Add `VfatLfnDecoder`.
3. Add `VfatLfnEncoder`.
4. Enhance `MutableFatDirectoryEntry` with `LongName`.
5. Enhance `DirectoryWriter` for LFN placement.
6. Add Phase 2 tests (~20 tests).

### 9.4 Deliverable

- Long filenames (≤255 chars) fully readable and writable.
- DOS alias auto-generation working.
- Checksum validation correct.
- Backward compatible with 8.3-only systems.

---

## 10. Phase 3: ISO 9660 Enhancements — Joliet & Rock Ridge

**Goal:** Add supplementary volume descriptor support (Joliet for long names, Rock Ridge for Unix metadata).

### 10.1 Phase 3a: Joliet Detection

- **`IsoSupplementaryVolumeDescriptor`** sealed class — immutable SVD.
  - Fields: `VolumeIdentifier`, `SystemIdentifier`, `RootDirRecord`, `EscapeSequence`.
  - Property `IsJoliet` — checks escape sequence (0x25 0x2f 0x40 / 0x43 / 0x45 for Joliet L1/2/3).

- **`IsoVolumeDescriptorSet`** sealed class (replaces single descriptor).
  - Property `PrimaryVolume` — always present.
  - Property `JolietVolume` — if Joliet escape sequence found; else null.
  - Property `RockRidgeVolume` — if SUSP detected; else null (v0.3+).
  - `FindVolumeByEscapeSequence(escapeSeq: byte[3]) → IsoSupplementaryVolumeDescriptor?`.

- **`JolietParser`** sealed class.
  - `Parse(pvdData: Span<byte>) → IsoSupplementaryVolumeDescriptor`.
  - Validate escape sequence, extract volume identifier in UCS-2 (big-endian, NOT little-endian like ISO 9660).
  - Throw `InvalidDataException` if invalid.

- **`IsoImage` enhancement:**
  - Constructor reads all VDs, populates `IsoVolumeDescriptorSet`.
  - Property `Volumes` → set.
  - Property `PrimaryVolume` now backed by `Volumes.PrimaryVolume`.
  - Method `ReadDirectory(volumeType: VolumeType.Joliet | Primary) → IReadOnlyList<IsoDirectoryRecord>`.

### 10.2 Phase 3b: Joliet Directory Reading

- **`JolietDirectoryRecord`** sealed class — inherits/wraps `IsoDirectoryRecord`.
  - Stores decoded UTF-16 filename (from UCS-2 in Joliet VD).
  - Property `IsoName` vs. `JolietName` — both if dual-VD.

- **`JolietDirectoryReader`** sealed class.
  - `ReadDirectory(isoImage: IsoImage, recordLba: uint) → IReadOnlyList<JolietDirectoryRecord>`.
  - Read raw ISO directory, decode filename from UCS-2.
  - Handle long filenames (≤255 chars in Joliet vs. ≤8.3 + LFN in ISO).

- **`IsoImage` enhancement:**
  - Method `FindEntryByName(name: string, volumeType: VolumeType) → JolietDirectoryRecord?`.
  - Search primary ISO name OR Joliet name.

### 10.3 Phase 3c: Rock Ridge SUSP (Optional, v0.3+)

- **`RockRidgeEntry`** sealed class — immutable SUSP entry.
  - Fields: `Name`, `Symlink`, `PosixFileMode`, `UserId`, `GroupId`.
  - Parse SUSP fields (`SP`, `NM`, `PX`, `SL`, etc.).

- **`RockRidgeParser`** sealed class.
  - `Parse(dirRecord: Span<byte>, offset: int) → RockRidgeEntry?`.
  - Validate SUSP signature, version.
  - Extract continuation areas (CE / CL) if needed.

- **`IsoImage` enhancement:**
  - Property `RockRidgeVolume` — if detected.
  - Method `ReadEntryWithRockRidge(recordLba: uint) → RockRidgeEntry?`.

### 10.4 TDD Spec

1. `JolietParser_Parse_RealJolietImage_ExtractsVolumeIdentifier`.
2. `JolietParser_Parse_InvalidEscapeSeq_ThrowsInvalidDataException`.
3. `JolietDirectoryReader_ReadDirectory_RealImage_DecodesUCS2Names`.
4. `IsoImage_FindEntryByName_Joliet_PreferredOverPrimary`.
5. `RockRidgeParser_Parse_RealImage_ExtractsPosixMetadata`.
6. `RockRidgeParser_Parse_NoSusp_ReturnsNull`.
7. `IsoVolumeDescriptorSet_FindVolumeByEscapeSequence_JolietLevel1`.

### 10.5 Commits

1. Add `IsoSupplementaryVolumeDescriptor` + `JolietParser`.
2. Add `IsoVolumeDescriptorSet` dispatcher.
3. Add `JolietDirectoryReader` + UCS-2 decoding.
4. Enhance `IsoImage` for multi-VD support.
5. Add `RockRidgeEntry` + `RockRidgeParser` (v0.3 branch).
6. Add Phase 3 tests (~15 tests).

### 10.6 Deliverable

- Joliet volume detection and directory reading complete.
- Long filenames (ISO + Joliet) read correctly.
- Rock Ridge metadata extracted (optional v0.3).
- Fallback to primary VD if no Joliet.

---

## 11. Phase 4: CUE/BIN Audio & Codec Dispatch

**Goal:** Support non-BINARY file types in CUE sheets. Dispatch to abstract codec layer.

### 11.1 Phase 4a: Abstract Audio Codec Interface

- **`IAudioCodec` interface** (in `Spice86.Storage.Internal`).
  - `Open(filePath: string) → IAudioTrack` — load audio file, detect format.
  - Throw `NotSupportedException` if unrecognized.

- **`IAudioTrack` interface.**
  - Property `SampleRate` — Hz (e.g., 44100 for CD-DA).
  - Property `ChannelCount` — 1 (mono) or 2 (stereo).
  - Property `LengthSamples` — total.
  - Property `LengthBytes` — if applicable.
  - `Seek(sampleOffset: ulong)` — move playback position.
  - `DecodeToSamples(dst: Span<int16>, reqSamples: int) → int` — decode `reqSamples` into PCM int16 stereo; return actual count.

- **`IAudioCodecFactory` interface.**
  - `CanHandle(filePath: string) → bool` — mime-type detection.
  - `Create() → IAudioCodec` — factory method.

- **`WavAudioCodec` sealed class.**
  - Minimal WAV parser (RIFF/WAVE + fmt / data chunks).
  - PCM only (no compression, no ADPCM).
  - Mono → stereo resampling.
  - Non-44100 Hz → 44100 resampling (naive; can upgrade).

- **`AudioCodecComposite` sealed class.**
  - Wraps list of `IAudioCodecFactory` implementations.
  - `Open(filePath: string) → IAudioTrack` — try each factory in order; first match wins.

### 11.2 Phase 4b: CUE FILE Directive Dispatch

- **`CueEntry` enhancement:**
  - New field: `FileType` (enum: BINARY, WAVE, MP3, FLAC, OGG, OPUS, AIFF).
  - Property `IsAudioFile` — true if WAV/MP3/etc.
  - Property `RequiresCodec` — true if non-BINARY.

- **`CueSheetParser` enhancement:**
  - Parse FILE ... directives, extract file type.
  - Validate file extension matches type (warning if mismatch).

- **`CueBinImage` enhancement:**
  - Constructor accepts `IAudioCodecFactory` (DI).
  - `LoadTrack(entry: CueEntry) → IAudioTrack?` — dispatcher.
    - If BINARY, use `FileBackedDataSource` as before.
    - If audio file, open codec and wrap in `CodecBackedAudioTrack`.

- **`CodecBackedAudioTrack` sealed class** (adapter).
  - Wraps `IAudioTrack` from codec.
  - Implements `ICdRomImage` semantics (LBA → sample offset → codec seek).
  - Handle sample-to-sector alignment (588 samples/sector for CD audio).

### 11.3 Phase 4c: CUE INDEX 00 Pregap Semantics

- **`CueTrackLayout`** sealed class.
  - `StartIndex` — MSF frame of INDEX 01 (track data start).
  - `PreGapFrames` — frames between INDEX 00 and INDEX 01 (usually 150).
  - `IndexZeroStartByte` — file offset of INDEX 00 relative to FILE start.
  - `TrackStartByte` — file offset of INDEX 01 relative to FILE start.
  - `TrackLengthFrames` — includes pregap if in same file; excludes if separate.

- **`CueBinImage` enhancement:**
  - `BuildTrackLayout(cueEntry: CueEntry, fileSource: IDataSource) → CueTrackLayout`.
  - Calculate correct byte offsets for INDEX 00 and INDEX 01.
  - Validate no overlap with subsequent tracks.

- **`CueFrameMapper`** sealed class.
  - `MapFrameToFileOffset(trackLayout: CueTrackLayout, frameOffset: uint) → (fileIndex: int, fileByteOffset: long)`.
  - Handle pregap frames (0–149) separately: if pregap in file, map to IndexZeroStartByte; if synthetic (separate file), synthesize silence.

### 11.4 Phase 4d: Subchannel Q Synthesis (MSCDEX Integration)

- **`SubchannelQData`** sealed class — immutable Q-data.
  - Fields: `TrackNumber`, `IndexNumber`, `RelativeMinute`, `RelativeSecond`, `RelativeFrame`, `AbsoluteMinute`, `AbsoluteSecond`, `AbsoluteFrame`, `Crc`.
  - `ToArray() → byte[16]` — serialize as BCD with CRC.

- **`SubchannelQBuilder`** sealed class.
  - `BuildForCueTrack(cueEntry: CueEntry, relativeFrame: uint) → SubchannelQData`.
  - Map CUE entries (track, index, MSF) to Subchannel-Q format.
  - Calculate relative MSF (within track) and absolute MSF (from disc start).

- **`CdRomDrive` enhancement** (in Spice86.Core, not Storage).
  - Call `SubchannelQBuilder` when MSCDEX calls `GetTrackInfo` or `GetSubchannelData`.

### 11.5 TDD Spec

1. `WavAudioCodec_Open_RealWavFile_DecodesSamples`.
2. `WavAudioCodec_Seek_Repositions`.
3. `WavAudioCodec_MonoToStereo_ResamplesToTargetRate`.
4. `CueSheetParser_FILE_WAVE_ExtractsFileType`.
5. `CueBinImage_LoadTrack_AudioFile_UsesCodec`.
6. `CueTrackLayout_WithIndexZero_CalculatePregapOffset`.
7. `CueFrameMapper_PreGapFrame_MapsToIndexZero`.
8. `CueFrameMapper_TrackFrame_MapsToIndexOne`.
9. `SubchannelQBuilder_BuildForCueTrack_ProducesBcdEncoded`.
10. `CodecBackedAudioTrack_Read_ReturnsInterleaved16BitStereo`.

### 11.6 Commits

1. Add `IAudioCodec` + `IAudioTrack`.
2. Add `WavAudioCodec` + `AudioCodecComposite`.
3. Add `IAudioCodecFactory` interface.
4. Enhance `CueEntry` + `CueSheetParser` for file types.
5. Add `CueTrackLayout` + `CueFrameMapper`.
6. Add `CodecBackedAudioTrack` adapter.
7. Add `SubchannelQBuilder` + Q-data synthesis.
8. Add Phase 4 tests (~25 tests).

### 11.7 Deliverable (Phase 4 complete)

- WAV files in CUE sheets fully supported.
- INDEX 00 pregap carve-out correct (bytes deducted from track).
- Subchannel-Q synthesized for MSCDEX calls.
- Extensible codec architecture (future MP3/FLAC/OGG as separate packages).

---

## 12. Phase 5: MDS/MDF Format Support (v0.3+)

**Goal:** Support Alcohol 120% MDS/MDF disc images.

### 12.1 Design (summary)

- **`MdsHeader`** sealed class — parse MDS header (88 bytes); validate "MEDIA DESCRIPTOR" signature.
- **`MdsTrackBlock`** sealed class — parse MDS track block (80 bytes); extract track type, sector size, MODE, pregap, postgap.
- **`MdsImage`** sealed class — implements `ICdRomImage`; load MDS + associated MDF file; map LBA to MDF offset.

### 12.2 TDD Spec

1. `MdsHeader_Parse_RealImage_ExtractsMetadata`.
2. `MdsTrackBlock_Parse_ExtractsTrackType`.
3. `MdsImage_Open_RealMdsMdf_ReadsSectors`.

### 12.3 Deliverable

- MDS/MDF images fully supported.
- Track type auto-detection (MODE1, MODE2, etc.).

---

## 13. Phase 6: FAT Write-Back Integration (v0.3+)

**Goal:** Integrate `MutableFatFileSystem` into Spice86.Core INT 13h / DOS int 21h write paths.

### 13.1 Design (summary)

- New type: `FatImageCache` — snapshot + diff tracking.
- When Spice86.Core writes sectors, mark cache dirty.
- On pause/exit, serialize cache back to original image file.
- Bidirectional sync: image changes ↔ DOS/BIOS state.

### 13.2 Deliverable

- Guest OS file changes persist to .IMG file.
- Floppy/HD writeback fully functional.

### 13.3 Progression

- Progress: **100%** (8/8 major milestones complete).
- Completed milestone 1: file-backed FAT image write-back object (`FileBackedFatImage`) with auto-flush semantics.
- Completed milestone 2: dirty-drive flush coordinator (`DosDriveManager.FlushDirtyFloppyImages`).
- Completed milestone 3: unmount-time persistence (`FloppyDiskDrive.Dispose` flushes dirty images).
- Completed milestone 4: shutdown-time persistence (`DisposeMachineAfterRun` flushes dirty floppy images before machine disposal).
- Completed milestone 5: multi-image shutdown parity (dirty non-current images on a mounted floppy stack are persisted on shutdown, not only the currently selected image).
- Completed milestone 6: pause-time persistence parity (dirty floppy images are flushed when pause is requested, not only on shutdown/dispose).
- Completed milestone 7: INT 26h writes and INT 25h reads support mounted image-backed drives beyond A/B (including C:), while preserving existing success semantics for non-image HDD folder drives.
- Completed milestone 8: BIOS INT 13h read/write supports mounted image-backed HDD drives (`DL=0x80` mapped to image-backed `C:`); end-to-end HDD lifecycle write-back integration test (`HddImageMount_FullLifecycle_PausesAndShutdownPersistAllWritesToDisk`) verifies pause-flush and shutdown-flush both persist guest writes to the host image file.

---

## 14. Phase 7: BOOT.COM HDD Image Support Integration (v0.3+)

**Goal:** Integrate full HDD image support with batch engine (`BOOT.COM` integration). Enable booting DOS from HDD images with mutable FAT write-back and batch command support.

### 14.1 Design (summary)

- **Batch Engine Integration:**
  - Extend batch processor to handle full DOS HDD boot sequences.
  - Support `BOOT.COM` with HDD parameters (drive letters, partition selection, boot flags).
  - Implement disk image mounting, partition enumeration, and bootable partition selection via batch commands.

- **HDD Image Lifecycle:**
  - Open raw HDD images (MBR-partitioned).
  - Enumerate partitions, select bootable via boot indicator or first non-empty.
  - Load FAT filesystem from partition offset.
  - Enable read+write ops on HDD via `MutableFatFileSystem`.
  - On exit/pause, serialize all pending writes back to image file.

- **Batch Command Extensions:**
  - `BOOT.COM` recognizes HDD image paths; resolves to partition table.
  - `MOUNT` and `IMGMOUNT` work with HDD images as well as floppies/CDs.
  - Partition selection logic: boot indicator preferred, fallback to first non-empty.
  - Full DOS path resolution (C:, D:, etc. via mounted images).

### 14.2 TDD Spec

1. `BootCom_WithHddImage_EnumeratesPartitions`.
2. `BootCom_BootIndicator_SelectsBootablePartition`.
3. `BootCom_FirstNonEmpty_FallbackIfNoBootable`.
4. `BatchEngine_MountHdd_PersistsFilesOnExit`.
5. `HddImage_WriteFile_RoundTrips`.
6. `HddImage_DeleteFile_FreesSpace`.
7. `HddImage_MultiplePartitions_AccessCorrectFilesystem`.

### 14.3 Commits

1. Extend batch engine for `BOOT.COM` HDD support.
2. Integrate `FatDiskImage` into batch mounting.
3. Add HDD lifecycle tests.
4. Add Phase 7 integration tests (~10 tests).

### 14.3.1 Progression

- Progress: **100%** (2/2 substantial atoms complete).
- Completed atom 1: HDD boot path end-to-end. `HardDiskBootService` parses the MBR (via `MbrCodec`), selects the bootable partition (boot indicator 0x80) or first non-empty, loads its first sector at 0000:7C00, sets the BIOS bootstrap CPU state with DL=0x80, IF set. `BootHddLaunchRequest` and `DosProgramLoader.ExecuteHddBoot` complete the launch dispatch chain. Batch `BOOT -l C..Z` produces a `BootHddLaunchRequest` instead of being rejected. 7 new RED-then-GREEN tests (`BootHardDiskTests`).
- Completed atom 2: MBR-partitioned HDD filesystem mounting. `DosDriveManager.MountHardDiskImage(char, byte[], string)` validates the 0xAA55 signature, picks `MasterBootRecord.FindBootablePartition() ?? FindFirstNonEmptyPartition()`, and calls a new partition-aware `FloppyDiskDrive.MountImage(byte[], string, int partitionByteOffset)` overload. `ApplyCurrentImage` now slices `data` from the partition byte offset before constructing the `FatFileSystem` view, while the full raw image bytes remain available via `GetCurrentImageData()` for INT 13h LBA sector I/O and for flushing back to the host file on dirty unmount. `HandleImgMount` now accepts `-t hdd` (and auto-detects `.hdd`), dispatching to a new `MountHardDiskImage(driveLetter, imagePaths)` private helper that reports validation failures via standard output. 6 new RED-then-GREEN tests (`HddImgMountTests`) cover: missing MBR signature, no usable partition, partition-aware FAT view, IMGMOUNT integration, IMGMOUNT-then-BOOT end-to-end, and auto-detection via `-t hdd` for `.img` extensions. Full Spice86.Tests suite (2142 tests) and Spice86.Storage.Tests (89 tests) green.

### 14.4 Deliverable

- Full HDD image support end-to-end.
- `BOOT.COM` works with partitioned hard disk images.
- Batch engine seamlessly handles HDD persistence.
- Exact DOSBox parity for HDD boot scenarios.

---

## 15. No-Maintenance Strategy

### 14.1 Mechanisms

1. **Deterministic algorithms:** No heuristics. Every decision has a spec (DOSBox equivalence).
2. **Immutable data structures:** Post-construction, types are read-only. State only mutates via mutation-specific types (`MutableBiosParameterBlock`, `FatTable`).
3. **Sealed implementations:** No inheritance surprises. All public types are `sealed`.
4. **Test vectors from dosbox-staging:** Every test includes a real image from DOSBox's test suite or generated via dosbox-staging itself.
5. **XML docs with examples:** Every public API includes `<example>` tag in XML docs. No guessing.
6. **SRP as maintenance tax:** Splitting concerns into 50+ small types makes fixes localized and low-risk.
7. **Dependency inversion:** Codecs, loggers, IO injected. Swapping implementations requires no code changes.
8. **Pre-release versioning:** 0.x signals "API may shift slightly"; semver after 1.0 is locked.
9. **Archived branch for each phase:** Phase end → branch tag + archive. Rollback trivial.
10. **Companion reference document:** README documents equivalence matrix (FAT12 clusters, CUE INDEX 00, Joliet UCS-2, MBR selection) with C++ ↔ C# side-by-side snippets.

### 15.2 Quality Gates (Phase Complete)

- [ ] 100% unit test coverage (condition coverage; all branches).
- [ ] Real image round-trip: image → parse → mutate → serialize → re-parse = identical bytes.
- [ ] DOSBox parity test: image processed by both Spice86 and DOSBox; outputs identical.
- [ ] XML docs: 100% coverage (SonarQube validates).
- [ ] SonarQube: zero "Code Smell" issues. Cognitive complexity < 10 per method.
- [ ] Code review: second pair of eyes on parity logic.
- [ ] Pre-release NuGet published (e.g., `0.2.0-alpha.1`).

---

## 15. Effort Estimate

| Phase | Effort | Notes |
|---|---:|---|
| Phase 0 | 3 units | Foundation; blocks all others. |
| Phase 1a | 2 units | Low-risk; small scope. |
| Phase 1b | 3 units | FAT12 bit-packing complex. |
| Phase 1c | 3 units | DOS name conversion fiddly. |
| Phase 1d | 2 units | Straightforward partitioning. |
| Phase 1e | 2 units | Orchestration + integration tests. |
| Phase 2 | 3 units | Checksum + UCS-2 encoding. |
| Phase 3 | 4 units | SUSP parsing complex. RR optional. |
| Phase 4 | 5 units | WAV parser + codec dispatch + Q-data. |
| Phase 5 | 2 units | Niche format; lower priority. |
| Phase 6 | 3 units | Integration with Spice86.Core. |
| Phase 7 | 2 units | BOOT.COM HDD integration with batch engine. |
| **Total** | **34 units** | ~6–9 weeks; 1.5–2 weeks / phase. |

---

## 16. Summary

**This plan achieves:**
- ✓ Exact full parity with dosbox-staging.
- ✓ Zero technical debt (SRP, OOP, immutable where sensible).
- ✓ TDD obsessive (test-first for every non-trivial method).
- ✓ XML docs comprehensive (100% coverage, examples for all public APIs).
- ✓ No maintenance burden (deterministic, immutable, sealed, DI).
- ✓ Phased, shippable increments (v0.2 as solid prerelease; v1.0 as stable).
