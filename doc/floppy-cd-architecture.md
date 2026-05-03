# Floppy, CD-ROM, FAT12, and Disc-Switching Architecture

_This document reviews every subsystem introduced or reworked by the
`implement-mscdex-support` branch. It covers the implemented components,
the architectural decisions behind them, known limitations, and the tests
that validate each area._

---

## Table of Contents

1. [Layer model](#1-layer-model)
2. [IFloppyDriveAccess — BIOS/DOS inversion](#2-ifloppydriveaccess--bios--dos-inversion)
3. [INT 13h floppy handler](#3-int-13h-floppy-handler)
4. [FAT12 file-system parsing](#4-fat12-file-system-parsing)
5. [FloppyDiskDrive and DosDriveManager](#5-floppydiskdrive-and-dosdrivemanager)
6. [CD-ROM image layer](#6-cd-rom-image-layer)
7. [MSCDEX (INT 2Fh AH=15h)](#7-mscdex-int-2fh-ah15h)
8. [Multi-image disc switching and Ctrl-F4](#8-multi-image-disc-switching-and-ctrl-f4)
9. [MOUNT / IMGMOUNT batch commands](#9-mount--imgmount-batch-commands)
10. [UI drive-status indicators](#10-ui-drive-status-indicators)
11. [Dependency-injection wiring](#11-dependency-injection-wiring)
12. [Test coverage](#12-test-coverage)
13. [Known limitations and pending work](#13-known-limitations-and-pending-work)

---

## 1. Layer model

Real DOS ran on top of BIOS services; BIOS had no knowledge of DOS. The
dependency hierarchy is:

```
┌─────────────────────────────────────────────┐
│  DOS programs / COMMAND.COM                 │
├─────────────────────────────────────────────┤
│  DOS kernel  (INT 21h, INT 2Fh/MSCDEX)      │
│  DosDriveManager  ←implements→              │
│       IFloppyDriveAccess                    │
├─────────────────────────────────────────────┤
│  BIOS  (INT 13h)                            │
│  SystemBiosInt13Handler uses only           │
│       IFloppyDriveAccess                    │
├─────────────────────────────────────────────┤
│  Hardware abstraction                       │
│  FloppyDiskDrive  /  CdRomDrive             │
│  Fat12FileSystem  /  CueBinImage / IsoImage │
└─────────────────────────────────────────────┘
```

`IFloppyDriveAccess` lives in `Spice86.Core.Emulator.Devices.Storage`
(a neutral hardware namespace). `SystemBiosInt13Handler` depends only on
that interface, never on any `Spice86.Core.Emulator.OperatingSystem` type.
`DosDriveManager` implements the interface, so the compile-time dependency
arrow runs **DOS → hardware contract ← BIOS**, not BIOS → DOS.

This mirrors how DOSBox Staging works: `bios_disk.cpp` operates on a
`DriveGeometry` struct and a raw byte buffer taken from the DOS-maintained
`imageDiskList[]` array, never calling into `DOS_Drive` directly.

---

## 2. IFloppyDriveAccess — BIOS / DOS inversion

```
Spice86.Core.Emulator.Devices.Storage
└── IFloppyDriveAccess
      ├── TryGetGeometry(driveNumber, out cylinders, out heads,
      │                  out sectorsPerTrack, out bytesPerSector)
      ├── TryRead(driveNumber, imageByteOffset, destination[], …)
      └── TryWrite(driveNumber, imageByteOffset, source[], …)
```

**Implementor:** `DosDriveManager`

```
DosDriveManager.TryGetGeometry
  └── TryResolveFloppyImage(driveNumber)
        ├── maps driveNumber 0 → 'A', 1 → 'B'
        ├── looks up _floppyDriveMap[driveLetter]
        └── calls BiosParameterBlock.Parse on image sector 0
```

`TryRead` and `TryWrite` operate directly on the live `byte[]` returned by
`FloppyDiskDrive.GetCurrentImageData()`, so writes are immediately
reflected in the in-memory floppy image (no separate write-back step).

---

## 3. INT 13h floppy handler

`SystemBiosInt13Handler` handles the following subfunctions:

| AH   | Name                  | Notes                                          |
|------|-----------------------|------------------------------------------------|
| 0x00 | Reset Disk System     | Always succeeds; CF cleared                   |
| 0x01 | Get Last Status       | Returns per-drive error code from `_lastStatus`|
| 0x02 | Read Sectors          | CHS → LBA, reads from `IFloppyDriveAccess`     |
| 0x03 | Write Sectors         | CHS → LBA, writes to `IFloppyDriveAccess`      |
| 0x04 | Verify Sectors        | Stub; succeeds when AL > 0                    |
| 0x08 | Get Drive Parameters  | Returns BPB-derived geometry; BL=0x04 (1.44MB)|
| 0x15 | Get Drive/Diskette Type | AH=0x02 (floppy) or 0x03 (HDD at 0x80)     |

CHS → LBA formula (1-based sector, standard BIOS):
```
LBA = (cylinder * heads + head) * sectorsPerTrack + (sector - 1)
```

Error codes are stored per BIOS drive number in `_lastStatus[0..1]` and
returned by AH=0x01. Drive numbers ≥ 0x80 are treated as hard disks; the
handler returns a generic hard-disk response for those.

---

## 4. FAT12 file-system parsing

```
Spice86.Core.Emulator.OperatingSystem.FileSystem
├── BiosParameterBlock        (parses boot sector bytes 0x00–0x23)
├── FatDirectoryEntry         (parses 32-byte directory entries)
└── Fat12FileSystem           (cluster-chain traversal, file/dir read)
```

### BiosParameterBlock fields

| Field              | Offset | Width | Description                         |
|--------------------|--------|-------|-------------------------------------|
| BytesPerSector     | 0x0B   | 2     | Normally 512                        |
| SectorsPerCluster  | 0x0D   | 1     | Power of 2                          |
| ReservedSectors    | 0x0E   | 2     | Sectors before FAT1                 |
| NumberOfFats       | 0x10   | 1     | Normally 2                          |
| RootEntryCount     | 0x11   | 2     | Max root-directory entries          |
| TotalSectors16     | 0x13   | 2     | 0 if > 65535 sectors                |
| SectorsPerFat      | 0x16   | 2     | Size of one FAT                     |
| SectorsPerTrack    | 0x18   | 2     | Used by INT 13h geometry            |
| NumberOfHeads      | 0x1A   | 2     | Used by INT 13h geometry            |

### Derived offsets

```
FAT1 start    = ReservedSectors
FAT2 start    = FAT1 start + SectorsPerFat
Root dir start = FAT2 start + SectorsPerFat
Data area start = Root dir start + ceil(RootEntryCount * 32 / BytesPerSector)
Cluster N start = Data area start + (N - 2) * SectorsPerCluster
```

### FAT12 cluster-chain resolution

Each FAT12 entry is 12 bits, packed in pairs over 3 bytes:

```
Even cluster N:  value = (byte[N*3/2]) | ((byte[N*3/2+1] & 0x0F) << 8)
Odd  cluster N:  value = (byte[N*3/2] >> 4) | (byte[N*3/2+1] << 4)
```

End-of-chain: value ≥ 0xFF8. Bad cluster: 0xFF7.

### FatDirectoryEntry

Each 32-byte entry:

| Offset | Width | Field        |
|--------|-------|--------------|
| 0      | 8     | Name (padded)|
| 8      | 3     | Extension    |
| 11     | 1     | Attributes   |
| 26     | 2     | First cluster|
| 28     | 4     | File size    |

Attribute flags: `0x10` = directory, `0x08` = volume label, `0x0F` = LFN
(skipped), `0xE5` first byte = deleted entry (skipped).

---

## 5. FloppyDiskDrive and DosDriveManager

### FloppyDiskDrive

```
FloppyDiskDrive
├── List<(byte[] Data, string Path)> _images
├── int _currentIndex
├── Fat12FileSystem? Image          (FAT12 view of current image)
├── MountImage(imageData, path)     (add + switch to new image)
├── AddImage(imageData, path)       (add without switching)
├── SwapToNextImage()               (cycle index, rebuild FAT12 view)
└── GetCurrentImageData() → byte[]? (raw bytes of current image)
```

`ApplyCurrentImage()` creates a `Fat12FileSystem` from the raw bytes and
updates `Label` from `Image.VolumeLabel`.

### DosDriveManager — drive maps

```
DosDriveManager
├── SortedDictionary<char, VirtualDrive>     _driveMap    (A–Z)
├── Dictionary<char, MemoryDrive>           _memoryDriveMap (Z: for AUTOEXEC)
└── Dictionary<char, FloppyDiskDrive>       _floppyDriveMap (A:, B:)
```

Key methods added in this PR:

| Method                                | Purpose                                  |
|---------------------------------------|------------------------------------------|
| `MountFloppyImage(letter, data, path)`| Creates a new `FloppyDiskDrive`, mounts  |
| `AddFloppyImage(letter, data, path)`  | Appends an image for disc switching      |
| `SwapFloppyDiscs()`                   | Calls `SwapToNextImage()` on all drives  |
| `MountFloppyFolder(letter, path)`     | Host-folder floppy; removes image entry  |
| `TryGetFloppyDrive(letter, out drive)`| Looks up `_floppyDriveMap`               |
| `MountFolderDrive(letter, path)`      | Mounts a host folder as a fixed drive    |

---

## 6. CD-ROM image layer

```
Spice86.Core.Emulator.Devices.CdRom.Image
├── ICdRomImage          (Tracks, TotalSectors, PrimaryVolume, Read, Dispose)
├── IsoImage             (flat ISO 9660 single-track)
└── CueBinImage          (multi-track BIN/CUE, audio + data)
      ├── CueSheetParser → CueSheet → List<CueEntry>
      ├── CdTrack        (Number, StartLba, LengthSectors, SectorSize, Mode)
      ├── FileBackedDataSource  (lazy-opened BIN file handle)
      └── SectorFraming  (cooked/raw/mode2form1 extraction helpers)
```

### CueBinImage track building

```
CUE file → CueSheetParser.Parse() → CueSheet
CueBinImage.BuildTracks(sheet):
  1. Group INDEX entries by TrackNumber
  2. For each track:
       a. Take file/mode/pregap/postgap from first INDEX entry
       b. Find INDEX 01 entry → startLba
  3. Derive length from next track's start (or file size for last track)
  4. Accumulate sector-size per BIN file (handle multi-file CUEs)
  5. Create CdTrack objects with pre-computed FileOffset
```

`ParseFileName` uses `Path.GetFullPath(raw, cueDir)` to safely combine the
CUE directory with the BIN filename, preventing `Path.Combine` from
silently dropping the directory when BIN paths contain a drive letter or
leading separator.

### Sector modes

| CdSectorMode    | Sector size | Description                       |
|-----------------|-------------|-----------------------------------|
| CookedData2048  | 2048        | Data only (ISO standard)          |
| Raw2352         | 2352        | Raw with 16-byte header           |
| Mode2Form1      | 2352        | XA Mode 2 Form 1 (24-byte header) |

---

## 7. MSCDEX (INT 2Fh AH=15h)

`MscdexService` is owned by the `Dos` class — created before `ProcessManager`
so it is always available when INT 2Fh is handled.

```
Dos constructor
  └── new MscdexService(state, memory, loggerService)
        ↓ passed to
  DosProcessManager → DosBatchExecutionEngine (IMGMOUNT registration)
        ↓
  DosInt2fHandler.Dispatch() calls mscdex.Dispatch()
```

### Implemented subfunctions

| AL   | Name                           |
|------|--------------------------------|
| 0x00 | Get number of CD-ROM drives    |
| 0x01 | Get CD-ROM drive device list   |
| 0x02–04 | File name info (stub)       |
| 0x05 | Read volume descriptor (VTOC)  |
| 0x08 | Absolute disk read             |
| 0x09 | Absolute disk write (error)    |
| 0x0B | CD-ROM drive check             |
| 0x0C | Get MSCDEX version (2.23)      |
| 0x0D | Get CD-ROM drive letters       |
| 0x0E | Get/Set volume descriptor pref |
| 0x0F | Get directory entry (not impl) |
| 0x10 | Send device driver request     |

The `SendDeviceDriverRequest` (AL=0x10) dispatcher supports IOCTL input
(device status, sector size, volume size, media changed, audio info) and
audio control commands (play, stop, resume).

### MscdexDriveEntry

```
MscdexDriveEntry
├── ICdRomDrive Drive
├── byte DriveIndex   (0-based, A=0)
└── char DriveLetter
```

`TryGetDrive` and `TryGetDriveByLetter` use `FirstOrDefault` on `_drives`.
`TryGetDriveBySubUnit` is an O(1) index lookup (subunit = position in list).

---

## 8. Multi-image disc switching and Ctrl-F4

```
IDiscSwapper (Spice86.Shared.Interfaces)
  └── SwapDiscImages()

Dos implements IDiscSwapper:
  SwapDiscImages()
    ├── MscdexService: foreach drive → drive.Drive.SwapToNextDisc()
    └── DosDriveManager.SwapFloppyDiscs()
          └── foreach floppy → floppy.SwapToNextImage()

MainWindowViewModel.OnKeyDown
  ├── if Ctrl+F4 → _discSwapper.SwapDiscImages() ; return (do not emit key)
  └── else → emit keyboard event to emulator
```

### CdRomDrive disc switching

```
CdRomDrive
├── List<ICdRomImage> _images
├── int _currentIndex
└── SwapToNextDisc()
      ├── MediaState.IsDoorOpen = true  (notify media change)
      ├── _currentIndex = (_currentIndex + 1) % _images.Count
      ├── _image = _images[_currentIndex]
      ├── MediaState.IsDoorOpen = false (notify media re-inserted)
      └── StopAudio()
```

### UI polling

The drive-status bar polls `IDriveStatusProvider.GetDriveStatuses()` every
second (via a Dispatcher timer in `DriveStatusViewModel`). `Dos` implements
this interface by iterating `DosDriveManager.FloppyDrives` and
`MscdexService.Drives`.

`DosVirtualDriveStatus` carries:

```
DosVirtualDriveStatus
├── DriveLetter
├── Type (Floppy / CdRom / HardDisk / Ram)
├── IsMediaPresent
├── CurrentImagePath
├── ImageCount
├── CurrentImageFileName    (for status-bar display)
└── HasMultipleImages       (enables Ctrl-F4 hint in tooltip)
```

---

## 9. MOUNT / IMGMOUNT batch commands

Both commands are implemented as batch command handlers in
`DosBatchExecutionEngine.CommandHandlers.cs`.

### MOUNT

```
MOUNT <drive> <path> [-t cdrom|floppy|hdd]
```

- Resolves `<path>` relative to the working directory.
- `-t cdrom` → creates an `IsoImage` from an `.iso` or a folder; registers
  a `MscdexDriveEntry`.
- `-t floppy` → calls `DosDriveManager.MountFloppyFolder`.
- `-t hdd` (default) → calls `DosDriveManager.MountFolderDrive`.

### IMGMOUNT

```
IMGMOUNT <drive> <image1> [<image2> …] -t floppy|iso|cue
```

- Multiple image paths mount all images to the same drive for disc
  switching; the first image becomes active.
- `-t floppy` → first image → `MountFloppyImage`; further images →
  `AddFloppyImage`.
- `-t iso` → `IsoImage` → `MscdexService.AddDrive`.
- `-t cue` → `CueBinImage` → `MscdexService.AddDrive`.
- Extension auto-detection: `.iso` → iso mode; `.cue` → cue mode;
  `.img` → floppy mode.

---

## 10. UI drive-status indicators

```
Spice86.ViewModels.DriveStatusViewModel
  ├── DispatcherTimer (1 s interval)
  ├── ObservableCollection<DosVirtualDriveStatus> DriveStatuses
  └── timer tick → IDriveStatusProvider.GetDriveStatuses()
                 → compare with previous snapshot
                 → update collection on change

Spice86.Views.UserControls.DriveStatusUserControl (AXAML)
  ├── ItemsControl bound to DriveStatuses
  ├── Each item: pill-shaped badge showing drive letter
  ├── Background: MediaPresenceBrushConverter (green/grey)
  └── Tooltip: current image file name (when HasMultipleImages)

MainWindow.axaml
  └── StatusBar → DriveStatusUserControl
```

`MediaPresenceBrushConverter` returns `Brushes.MediumSeaGreen` when
`IsMediaPresent` is true, otherwise `Brushes.DarkGray`.

---

## 11. Dependency-injection wiring

```
Spice86DependencyInjection (construction order for new components)

1. Memory, CPU, I/O, BIOS handlers (unchanged)
2. Dos (creates MscdexService internally)
     └── DosDriveManager
3. SystemBiosInt13Handler(…, dos.DosDriveManager, …)
     ← receives IFloppyDriveAccess; no DOS type visible to BIOS
4. interruptInstaller.InstallInterruptHandler(int13WithFloppy)
5. IDiscSwapper → dos (passed to MainWindowViewModel)
6. IDriveStatusProvider → dos (passed to DriveStatusViewModel)
```

The key ordering constraint: `SystemBiosInt13Handler` must be constructed
**after** `Dos` because it receives `DosDriveManager` cast to
`IFloppyDriveAccess`. Previously INT 13h was constructed before DOS.

---

## 12. Test coverage

| Test class                   | Count | What it covers                                           |
|------------------------------|-------|----------------------------------------------------------|
| `Int13FloppyTests`           | 9     | AH=0x00/01/02/03/08/15; error paths; boundary checks     |
| `BiosParameterBlockTests`    | 8     | Parse from raw bytes; various floppy geometries          |
| `FatDirectoryEntryTests`     | 7     | Attribute flags; deleted/LFN skip; name parsing          |
| `Fat12FileSystemTests`       | 12    | Root dir listing; cluster chain; file read; volume label |
| `DosDriveManagerFloppyTests` | 14    | Mount/add/swap images; TryGetGeometry; TryRead/TryWrite  |
| `CdRomDriveDiscSwapTests`    | 8     | Single/multi-image; swap cycling; audio stop on swap     |
| `MountBatchCommandTests`     | 8     | MOUNT/IMGMOUNT parsing; multi-image; extension detection |
| `MscdexDeviceDriverRequestTests` | (existing) | IOCTL sub-commands; audio commands          |
| `CdAudioPlayerTests`         | (existing) | Play/stop/pause/resume state machine             |

Total new tests: **66+** across the branch; full suite ≥ 1838 tests, 0
failures.

---

## 13. Known limitations and pending work

The following features were intentionally deferred to separate PRs due to
scope. Each requires significant standalone work.

### 13.1 Intel 8272A FDC (low-level floppy controller)

DOSBox Staging emulates the full NEC µPD765 / Intel 8272A FDC at I/O ports
0x3F0–0x3F7, with DMA channel 2 (8237 DMA controller), IRQ 6, and all
FDC command byte sequences (read/write/format/seek/recalibrate/sense
interrupt). This is approximately 2 000 lines and a separate subsystem.

The current implementation provides INT 13h BIOS-level floppy access
(which is sufficient for most DOS games); low-level direct-port FDC access
is not yet emulated.

### 13.2 Red Book CDDA audio

Reading audio sectors from CUE/BIN images, mixing them through the
`SoundChannel` feed loop, and supporting the MSCDEX `PlayAudio` IOCTL
subcommand with correct sample-rate conversion requires ~1 000 lines and
integration with the Bufdio audio layer. The architecture stubs
(`CdAudioPlayer`, `CdAudioPlayback`, `PlayAudio` / `StopAudio` / `ResumeAudio`
on `CdRomDrive`) are in place; the sample-streaming pipeline is pending.

### 13.3 Folder-as-image disc switching

Switching between multiple host-folder mounts (e.g. game disc 1 at
`C:\game\disc1\` and disc 2 at `C:\game\disc2\`) requires a virtual
ISO 9660 builder that constructs an in-memory image from a directory tree
at mount time. This is non-trivial and deferred.

### 13.4 FAT16/FAT32 floppy images

`Fat12FileSystem` supports FAT12 only (the correct choice for all standard
floppy sizes ≤ 2.88 MB). FAT16/FAT32 detection and parsing would be needed
only for large removable media, which is rare in DOS-era software.

### 13.5 Write-back to host file

`FloppyDiskDrive` stores image data as an in-memory `byte[]`. Writes via
INT 13h AH=0x03 update the in-memory bytes but do not flush back to the
original `.img` file on disk. A flush/sync method would be needed for games
that save to floppy and expect the change to persist across sessions.

---

_End of document_
