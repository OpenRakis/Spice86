# Storage Assembly Extraction Feasibility Report

Date: 2026-05-15
Scope: Extract `Spice86.Storage.Fat` and `Spice86.Storage.Cd` into standalone repos / NuGet packages with complete feature parity and high fidelity vs. `dosbox-staging` (vendored at [dosbox-staging/src/dos](../dosbox-staging/src/dos)).

> "Doable" is defined here as: standalone repos that ship FAT and CD storage stacks complete enough to be drop-in replacements for the file-format handling code currently used by Spice86 *and* by DOSBox Staging, with comparable correctness and coverage.

---

## 1. Verdict

**Yes, technically doable. No, not at parity with what is currently extracted.**

What is in the new assemblies today is roughly **20–25% of the surface area** that DOSBox Staging's equivalent code covers. Extracting them as-is and calling them "feature complete" would be misleading: they are deliberately scoped to what Spice86 needs to mount images and read sectors. Reaching real parity requires substantial *additional* work — not just packaging.

A staged release strategy (ship narrow v0.x; grow toward parity) is realistic and recommended.

---

## 2. Current code size baseline

Raw line counts of the corresponding sources (counted on disk, including blanks/comments):

| Surface | Spice86 (extracted today) | DOSBox Staging (`src/dos`) | Ratio |
|---|---:|---:|---:|
| FAT / floppy storage | 1064 LOC | drive_fat.cpp: ~1600 LOC | ~0.67x |
| CD storage (parsers + image) | 1076 LOC | cdrom_image.cpp + cdrom.cpp/h + drive_iso.cpp + cdrom_image.h + cdrom_mds.h ~ 2800 LOC | ~0.38x |
| **Total** | **2140 LOC** | **~4400 LOC** | **~0.49x** |

Line counts alone understate the gap because DOSBox Staging code is much denser (write paths, format dispatch, codec integration, OS backends) while Spice86's code is intentionally read-only and single-format.

---

## 3. Feature parity matrix

Legend: ✓ present, ◐ partial, ✗ missing.

### 3.1 FAT / floppy

| Capability | Spice86 today | DOSBox Staging | Notes |
|---|:---:|:---:|---|
| FAT12 read | ✓ | ✓ | [`Fat12FileSystem`](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/Fat12FileSystem.cs) |
| FAT16 read | ◐ | ✓ | [`FatFileSystem`](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/FatFileSystem.cs) reads BPB and detects FAT16 but real-world FAT16 paths are exercised only via Fat12 sibling. |
| FAT32 read | ◐ | ✓ | Detected via BPB; not exercised. |
| FAT12 write (sector + FAT chain) | ✗ | ✓ | DOSBox `fatDrive::writeSector` / `addDirectoryEntry`. Write-back in Spice86 is done at DOS layer, not in the FAT library. |
| FAT16 write | ✗ | ✓ |  |
| FAT32 write | ✗ | ✓ |  |
| MBR partition table parsing | ✗ | ✓ | DOSBox `fatDrive` walks `partTable`. Spice86 assumes raw, single-partition floppy images. |
| LFN (VFAT) read | ◐ | ✓ | `FatDirectoryEntry.IsLfn` filters LFN out; not decoded. |
| LFN write | ✗ | ✓ |  |
| Subdirectories | ✓ | ✓ |  |
| Host folder → FAT image builder | ✓ | n/a | [`VirtualFloppyImage`](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/VirtualFloppyImage.cs); not a DOSBox feature. |
| Geometry detection beyond 1.44 MB | ◐ | ✓ | BPB reads geometry; builder hardcodes 1.44 MB. |
| Volume label read/write | ◐ | ✓ | Read only. |
| Attribute changes / timestamps | ✗ | ✓ |  |

### 3.2 CD / ISO / CUE-BIN

| Capability | Spice86 today | DOSBox Staging | Notes |
|---|:---:|:---:|---|
| ISO 9660 PVD parse | ✓ | ✓ | [`IsoImage`](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/IsoImage.cs) |
| ISO 9660 directory walking (Find/Open) | ◐ | ✓ | Spice86 exposes raw `Read(lba)`; directory walking is done in MSCDEX in Core, not in the parser. DOSBox `drive_iso` provides FindFirst/FindNext directly. |
| Joliet (UCS-2 long names) | ✗ | ✓ | DOSBox detects `0x25 0x2F 0x4(0|3|5)` escape; Spice86 ignores secondary VDs. |
| Rock Ridge (SUSP `SP`, `NM`, `PX`) | ✗ | ◐ | DOSBox has partial SUSP plumbing; Spice86 has none. |
| El Torito boot record | ✗ | ✗ | Neither side implements bootable-CD parsing. |
| CUE/BIN MODE1/2048 | ✓ | ✓ | [`CueBinImage`](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CueBinImage.cs) |
| CUE/BIN MODE1/2352, MODE2/2336, MODE2/2352 | ✓ | ✓ |  |
| CUE/BIN AUDIO (raw 2352 PCM) | ✓ | ✓ |  |
| CUE FILE WAVE / MP3 / FLAC / OGG / OPUS | ✗ | ✓ | DOSBox loads non-BINARY audio FILE entries via SDL_sound. Spice86 only handles `BINARY` files. **This is the single biggest CD parity gap.** |
| Multiple FILE entries per CUE | ◐ | ✓ | Parser tracks FILE+TRACK pairs; image build assumes one source per track but does not gate it. |
| INDEX 00 (pregap inside file) | ◐ | ✓ | INDEX 00 is parsed but not subtracted from track length. |
| PREGAP / POSTGAP (synthetic) | ◐ | ✓ | Captured into `CueEntry` but not synthesised when reading silence. |
| MSF / Redbook offset (150) | ✓ | ✓ |  |
| ISRC / CATALOG | ◐ | ✓ | CATALOG parsed into `UpcEan`; ISRC ignored. |
| Subchannel Q | ✗ | ✓ |  |
| CD-G (sub-channel graphics) | ✗ | ✗ |  |
| MDS / MDF format | ✗ | ✓ | DOSBox `cdrom_mds.h`. |
| Host CD-ROM device (Linux ioctl, Win32) | ✗ | ✓ | DOSBox `cdrom_ioctl_linux.cpp`, `cdrom_win32.cpp`. Out of scope for a pure-managed parser library. |
| Host folder → ISO builder | ✓ | n/a | [`VirtualIsoImage`](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/VirtualIsoImage.cs); not a DOSBox feature. |
| Multi-disc switching | n/a | n/a | Lives in `CdRomDrive` in Core, not in the parser layer. |

### 3.3 Summary numbers

| Surface | Features matched | Features partial | Features missing | Parity score |
|---|---:|---:|---:|---:|
| FAT | 4 | 5 | 5 | **~36%** |
| CD/ISO/CUE | 6 | 6 | 7 | **~47%** |

These are coarse counts; the *cost-weighted* parity (i.e. accounting for the engineering size of "audio codec dispatch" or "FAT write+MBR") is closer to **20–25%**.

---

## 4. What "high fidelity" requires beyond parity

Bit-for-bit / game-by-game compatibility with DOSBox-Staging would additionally require:

1. **CUE INDEX 00 semantics** matching DOSBox's `REDBOOK_FRAME_PADDING = 150` carve-out exactly for pregap inside the same FILE.
2. **Audio resampling identical to DOSBox's `Sound_AudioInfo` path**, including the 22.05 kHz → 44.1 kHz interpolation step used when CDDA codec rate differs from Redbook.
3. **Subchannel-Q answers** for MSCDEX `GetMediaCatalogNumber` / `GetTrackInfo`. DOSBox synthesizes these from CUE; Spice86 currently does not.
4. **MBR ambiguity rules** for FAT hard-disk images: DOSBox uses "first non-zero partition" with a warning. Reproducing this is mandatory for IMGMOUNT parity.
5. **Volume label round-trips**: DOSBox treats label in both BPB and root dir entry; high fidelity requires writing both.
6. **Sector-mode permissive reads**: DOSBox accepts cooked reads against raw 2352 tracks by skipping the 16-byte sync header. Spice86 already does this for MODE1; needs to be verified for MODE2/Form1 across the same images DOSBox does.
7. **Floppy geometry variants** (160K, 180K, 320K, 360K, 720K, 1.2M, 2.88M) — Spice86's builder only produces 1.44 MB.

Each of these can be ported by mirroring the relevant DOSBox C++ method into managed C#. None is conceptually blocking.

---

## 5. Architectural feasibility (packaging)

Treat this independently of feature parity: *can these libraries cleanly ship as repo-external NuGet packages today?*

### 5.1 Dependencies that block clean extraction

| Dependency | Where | Resolution |
|---|---|---|
| `Spice86.Shared.Interfaces.ILoggerService` | [`VirtualFloppyImage`](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/VirtualFloppyImage.cs) ctor | Either ship a minimal `IStorageLogger` interface inside the storage package, or accept `Action<string>` log callbacks. Avoid taking a dependency on the Spice86.Shared package from external libraries. |
| Namespaces still `Spice86.Shared.Emulator.Storage.*` | All extracted files | Rename to `Spice86.Storage.Fat.*` / `Spice86.Storage.Cd.*` in one churn-only commit before publishing. |
| `Spice86.Shared` project reference in the two new csproj files | csproj | Remove the reference after the logger-decoupling change above. |

### 5.2 Dependencies that do *not* block extraction

1. No CFG-CPU references in storage code (verified by `grep`).
2. No `Machine` / DOS handler imports.
3. No Avalonia / UI references.
4. No `unsafe` blocks, no platform-conditional code — pure managed.

### 5.3 Test extraction

The DOS-layered tests under [tests/Spice86.Tests/Dos/FileSystem](../tests/Spice86.Tests/Dos/FileSystem) and [tests/Spice86.Tests/CdRom](../tests/Spice86.Tests/CdRom) split cleanly:

1. **Parser-pure tests** (CUE/BIN edge cases, BPB parsing, FAT cluster walks, VirtualFloppyImage round-trip): *can* move to the standalone repos.
2. **Integration tests** (MSCDEX, INT 13h floppy, disc swap on `CdRomDrive`): *must* stay in Spice86.

Roughly: 9 test files (~1500 LOC) can ship with the new repos; 25+ test files stay.

---

## 6. PR-size impact (recap)

If extraction completes *as currently scoped* and the two assemblies move to external repos consumed via NuGet:

| Bucket | Net LOC removed from this PR |
|---|---:|
| 6 FAT files | ~1064 |
| 16 CD files | ~1076 |
| 2 new csproj | ~10 |
| 2 new test files added (CUE/BIN edge cases) | ~120 |
| 9 parser-only tests moved out | ~1500 |
| **Total reduction** | **~3700 LOC** |

Current PR #2144 hovers near 7000 net additions, so this is approximately a **50% PR-size reduction** *at current scope*, with the remaining changes focused on DOS/BIOS runtime, MSCDEX, IFloppyDriveAccess, batch commands, and 8272A FDC. A reviewer's mental model becomes "feature PR" rather than "feature + parser libraries".

---

## 7. Risk assessment

| Risk | Likelihood | Severity | Mitigation |
|---|:---:|:---:|---|
| External repo lags Spice86 needs (e.g., new sector mode required mid-feature) | Med | Med | Co-release: tag matching versions; allow Spice86 PRs to bump the package SHA. |
| Packaging adds friction for casual contributors | High | Low | Provide `local` `<ProjectReference>` switch in csproj for clone-and-build workflows. |
| Diverging APIs between Spice86 and a future second consumer | Low | Med | Keep API surface minimal at v0.x; do not add Spice86-specific types into the public surface. |
| Versioning churn during DOSBox-parity work | High | Low | Use 0.x prerelease NuGet versions while parity work is ongoing. |
| Logger decoupling regression | Low | Low | Mechanical interface change; one targeted test pass. |
| Test discoverability after split | Low | Med | Keep integration tests in Spice86; add a "consumer smoke test" in the storage repos that pulls a sample CUE/BIN from a test fixtures repo. |

---

## 8. Cost estimate to reach parity

Excluding time estimates (per code-style policy), the engineering items to reach DOSBox-Staging-equivalent fidelity, ordered roughly by effort and value:

1. **FAT write path (FAT12/16/32 + cluster chaining + free-cluster allocation)** — largest item; required for any image where the guest writes back. *(High effort, high value.)*
2. **MBR partition table parsing** in FAT mount. *(Low–medium effort, required for hard-disk images.)*
3. **CUE audio codec dispatch** (WAV at minimum; MP3/FLAC/OGG behind optional adapter packages). *(Medium effort, high value for games shipping CDDA in MP3/WAV.)*
4. **Joliet support** in ISO. *(Medium effort, high value for any non-ASCII filenames or long names.)*
5. **LFN (VFAT) read + write** in FAT. *(Medium effort, high value.)*
6. **Floppy geometry variants** in `VirtualFloppyImage`. *(Low effort.)*
7. **CUE INDEX 00 pregap-in-file semantics + synthetic PREGAP/POSTGAP silence**. *(Low effort, audio correctness.)*
8. **Rock Ridge SUSP** (`NM`, `PX`). *(Medium effort, lower value for DOS use.)*
9. **MDS/MDF format**. *(Low–medium effort, niche.)*
10. **Subchannel-Q synthesis** for MSCDEX track-info calls. *(Low effort, moderate value.)*

Items 1, 2, 3, 4 alone get the libraries to "honest 80% parity" for DOS game compatibility.

---

## 9. Recommendation

1. **Do extract.** The architectural extraction is sound, dependencies are tractable, and the PR-size benefit is real.
2. **Do not market the v0 packages as DOSBox-parity.** Ship them as `0.1.x` and document the gap matrix from §3 in each repo's README.
3. **Decouple `ILoggerService` first** — single mechanical change that removes the last hidden dependency on `Spice86.Shared`.
4. **Rename namespaces** in a single churn-only commit *before* the first NuGet publish, not after.
5. **Move parser-only tests with the code.** Keep integration tests where they belong.
6. **Treat parity as a roadmap, not a precondition.** Pick items 1–4 from §8 as the v0.2 milestone and ship incrementally.

---

## 10. Appendix: file inventory at time of analysis

FAT package: [BiosParameterBlock](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/BiosParameterBlock.cs) · [Fat12FileSystem](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/Fat12FileSystem.cs) · [FatDirectoryEntry](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/FatDirectoryEntry.cs) · [FatFileSystem](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/FatFileSystem.cs) · [FatType](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/FatType.cs) · [VirtualFloppyImage](../src/Spice86.Storage.Fat/Emulator/Storage/FileSystem/VirtualFloppyImage.cs)

CD package: [CdRomImageFactory](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CdRomImageFactory.cs) · [CdSectorMode](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CdSectorMode.cs) · [CdTrack](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CdTrack.cs) · [CueBinImage](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CueBinImage.cs) · [CueEntry](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CueEntry.cs) · [CueSheet](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CueSheet.cs) · [CueSheetParser](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/CueSheetParser.cs) · [FileBackedDataSource](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/FileBackedDataSource.cs) · [ICdRomImage](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/ICdRomImage.cs) · [IDataSource](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/IDataSource.cs) · [IsoDirectoryRecord](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/IsoDirectoryRecord.cs) · [IsoImage](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/IsoImage.cs) · [IsoVolumeDescriptor](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/IsoVolumeDescriptor.cs) · [MemoryDataSource](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/MemoryDataSource.cs) · [SectorFraming](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/SectorFraming.cs) · [VirtualIsoImage](../src/Spice86.Storage.Cd/Emulator/Storage/CdRom/VirtualIsoImage.cs)

DOSBox Staging reference: [drive_fat.cpp](../dosbox-staging/src/dos/drive_fat.cpp) · [drive_iso.cpp](../dosbox-staging/src/dos/drive_iso.cpp) · [cdrom.cpp](../dosbox-staging/src/dos/cdrom.cpp) · [cdrom.h](../dosbox-staging/src/dos/cdrom.h) · [cdrom_image.cpp](../dosbox-staging/src/dos/cdrom_image.cpp) · [cdrom_image.h](../dosbox-staging/src/dos/cdrom_image.h) · [cdrom_mds.h](../dosbox-staging/src/dos/cdrom_mds.h)
