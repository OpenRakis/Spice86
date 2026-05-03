# Floppy, CD-ROM, FAT12/16/32, FDC, and Disc-Switching Architecture

_This document reviews every subsystem introduced or reworked by the
`implement-mscdex-support` branch. It covers all implemented components
and the architectural decisions behind them._

---

## Table of Contents

1. [Layer model](#1-layer-model)
2. [IFloppyDriveAccess — BIOS/DOS inversion](#2-ifloppydriveaccess--bios--dos-inversion)
3. [INT 13h floppy handler](#3-int-13h-floppy-handler)
4. [Intel 8272A FDC hardware emulation](#4-intel-8272a-fdc-hardware-emulation)
5. [FAT12/16/32 file-system parsing](#5-fat121632-file-system-parsing)
6. [Virtual disk images (FloppyImage, IsoImage)](#6-virtual-disk-images)
7. [Floppy write-back](#7-floppy-write-back)
8. [FloppyDiskDrive and DosDriveManager](#8-floppydiskdrive-and-dosdrivemanager)
9. [CD-ROM image layer](#9-cd-rom-image-layer)
10. [MSCDEX (INT 2Fh AH=15h)](#10-mscdex-int-2fh-ah15h)
11. [Multi-image disc switching and Ctrl-F4](#11-multi-image-disc-switching-and-ctrl-f4)
12. [MOUNT / IMGMOUNT batch commands](#12-mount--imgmount-batch-commands)
13. [UI drive-status indicators](#13-ui-drive-status-indicators)
14. [Dependency-injection wiring](#14-dependency-injection-wiring)
15. [Test coverage](#15-test-coverage)

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

## 4. Intel 8272A FDC hardware emulation

`FloppyDiskController` in `Spice86.Core.Emulator.Devices.ExternalInput` emulates
the Intel 82077AA / NEC µPD765 floppy disk controller at I/O ports 0x3F2–0x3F5.

### I/O port map

| Port | Direction | Name                     | Description                          |
|------|-----------|--------------------------|--------------------------------------|
| 0x3F2 | Write    | Digital Output Register  | Motor enable (bits 4-7), drive select (bits 0-1), reset (bit 2) |
| 0x3F4 | Read     | Main Status Register     | MRQ, DIO, CB, FDD busy flags         |
| 0x3F5 | R/W      | Data FIFO                | Command bytes in, result bytes out   |

### Main Status Register bits

| Bit | Name | Meaning                        |
|-----|------|--------------------------------|
| 7   | MRQ  | 1 = data register ready        |
| 6   | DIO  | 1 = FDC→CPU; 0 = CPU→FDC      |
| 4   | CB   | FDC busy executing command     |
| 0-3 | FDD  | FDD 0-3 in seek                |

### Supported commands

| Command byte | Name                   | Params | Result bytes |
|--------------|------------------------|--------|--------------|
| 0x03         | SPECIFY                | 2      | 0            |
| 0x04         | SENSE DRIVE STATUS     | 1      | 1 (ST3)      |
| 0x07         | RECALIBRATE            | 1      | 0 + IRQ6     |
| 0x08         | SENSE INTERRUPT STATUS | 0      | 2 (ST0, PCN) |
| 0x0F         | SEEK                   | 2      | 0 + IRQ6     |
| 0xE6         | READ DATA              | 8      | 7 + IRQ6 + DMA |
| 0xC5         | WRITE DATA             | 8      | 7 + IRQ6 + DMA |
| 0x4A         | READ ID                | 1      | 7 + IRQ6     |
| 0x4D         | FORMAT TRACK           | 5      | 7 + IRQ6     |

### DMA and IRQ integration

```
FloppyDiskController
├── _dmaChannel (DmaChannel 2, 8-bit, from DmaBus)
│     └── used for READ DATA / WRITE DATA transfers
│           read:  FDC reads sector, writes bytes to DMA buffer
│           write: FDC reads bytes from DMA buffer, writes sector
└── _raiseIrq (Action<byte> delegate)
      └── called after seek/recalibrate/read/write completes
            → raises IRQ 6 via Intel8259Pic
```

---

## 5. FAT12/16/32 file-system parsing

```
Spice86.Core.Emulator.OperatingSystem.FileSystem
├── BiosParameterBlock        (parses boot sector bytes 0x00–0x23 + FAT32 extension)
├── FatDirectoryEntry         (parses 32-byte directory entries)
├── FatType                   (enum: Fat12, Fat16, Fat32)
├── FatFileSystem             (unified FAT12/16/32 cluster-chain traversal)
└── Fat12FileSystem           (backward-compatible alias for FAT12 images)
```

### BiosParameterBlock fields

| Field              | Offset | Width | Description                                    |
|--------------------|--------|-------|------------------------------------------------|
| BytesPerSector     | 0x0B   | 2     | Normally 512                                   |
| SectorsPerCluster  | 0x0D   | 1     | Power of 2                                     |
| ReservedSectorCount| 0x0E   | 2     | Sectors before FAT1                            |
| NumberOfFats       | 0x10   | 1     | Normally 2                                     |
| RootEntryCount     | 0x11   | 2     | Max root-directory entries (0 for FAT32)       |
| TotalSectors16     | 0x13   | 2     | 0 if > 65535 sectors                           |
| SectorsPerFat16    | 0x16   | 2     | FAT12/16 size; 0 for FAT32                     |
| SectorsPerTrack    | 0x18   | 2     | Used by INT 13h geometry                       |
| NumberOfHeads      | 0x1A   | 2     | Used by INT 13h geometry                       |
| TotalSectors32     | 0x20   | 4     | Used when TotalSectors16 == 0                  |
| SectorsPerFat32    | 0x24   | 4     | FAT32 only (BPB extended)                      |
| RootCluster        | 0x2C   | 4     | FAT32 only: cluster number of root directory   |

### FAT type detection (Microsoft specification)

```
rootDirSectors = ceil(RootEntryCount * 32 / BytesPerSector)
fatSz          = SectorsPerFat16 != 0 ? SectorsPerFat16 : SectorsPerFat32
totalSectors   = TotalSectors16 != 0  ? TotalSectors16  : TotalSectors32
dataSectors    = totalSectors - (ReservedSectorCount + NumberOfFats * fatSz + rootDirSectors)
clusterCount   = dataSectors / SectorsPerCluster

clusterCount <  4085  → FAT12
clusterCount < 65525  → FAT16
else                  → FAT32
```

### FAT cluster-chain resolution

| FAT type | Entry width | End-of-chain    | Bad cluster |
|----------|-------------|-----------------|-------------|
| FAT12    | 12 bits     | ≥ 0xFF8         | 0xFF7       |
| FAT16    | 16 bits     | ≥ 0xFFF8        | 0xFFF7      |
| FAT32    | 28 bits     | ≥ 0x0FFFFFF8    | 0x0FFFFFF7  |

FAT12 entry packing:
```
Even cluster N:  value = (byte[N*3/2]) | ((byte[N*3/2+1] & 0x0F) << 8)
Odd  cluster N:  value = (byte[N*3/2] >> 4) | (byte[N*3/2+1] << 4)
```

### Derived sector offsets

```
FAT1 start     = ReservedSectorCount
Data area start = ReservedSectorCount + NumberOfFats * fatSz + rootDirSectors
Cluster N start = DataAreaStart + (N - 2) * SectorsPerCluster

FAT12/16 root  = FAT1 start + NumberOfFats * fatSz  (fixed region)
FAT32 root     = cluster chain starting at RootCluster
```

### FatDirectoryEntry

Each 32-byte entry:

| Offset | Width | Field        |
|--------|-------|--------------|
| 0      | 8     | Name (padded)|
| 8      | 3     | Extension    |
| 11     | 1     | Attributes   |
| 26     | 2     | First cluster (low word) |
| 20     | 2     | First cluster (high word, FAT32) |
| 28     | 4     | File size    |

Attribute flags: `0x10` = directory, `0x08` = volume label, `0x0F` = LFN
(skipped), `0xE5` first byte = deleted entry (skipped).

---

## 6. Virtual disk images

### VirtualFloppyImage

`VirtualFloppyImage` builds a 1.44 MB FAT12 floppy image in memory from a
host directory. The resulting `byte[]` can be passed to
`FloppyDiskDrive.MountImage()` and supports disc switching.

```
VirtualFloppyImage.Build(sourceDir, logger) → byte[1,474,560]
  1. Write BPB in boot sector (sector 0)
  2. Zero FAT1 and FAT2 (sectors 1–18), write media descriptor 0xF0
  3. For each file in sourceDir (and one level of subdirs):
       a. Allocate clusters for file data
       b. Write FAT chain entries
       c. Write directory entries in root dir region
       d. Write file data in data clusters
  4. Files that would exceed available clusters → skip + log warning
```

Layout of a 1.44 MB FAT12 image:

```
Sector 0        : Boot sector / BPB
Sectors 1-9     : FAT1 (9 sectors)
Sectors 10-18   : FAT2 (copy)
Sectors 19-32   : Root directory (14 sectors, 224 entries)
Sectors 33-2879 : Data area (clusters 2-2848)
```

### VirtualIsoImage

`VirtualIsoImage` implements `ICdRomImage` and builds a minimal ISO 9660
image in memory from root-level files in a host directory.

```
VirtualIsoImage(sourceDir, volumeLabel) → ICdRomImage
  1. Write system area (sectors 0-15) as zeroes
  2. Sector 16: Primary Volume Descriptor
  3. Sector 17: Volume Descriptor Set Terminator
  4. Sector 18: Path table (LE)
  5. Sector 19: Path table (BE)
  6. Sector 20: Root directory records
  7. Sectors 21+: File data (one file per sector sequence)
```

Both `VirtualFloppyImage` and `VirtualIsoImage` enable disc switching for
folder mounts: each folder is converted to an in-memory image that can be
loaded into `FloppyDiskDrive` or `CdRomDrive` just like a `.img`/`.iso` file.

---

## 7. Floppy write-back

`FloppyDiskDrive` tracks whether its in-memory image has been modified and can
flush the changes back to the original file:

```
FloppyDiskDrive
├── bool IsDirty           (true after any successful TryWrite)
├── MarkDirty()            (called by DosDriveManager.TryWrite)
└── FlushToDisk()          (writes _images[_currentIndex].Data to Path)
```

`DosDriveManager.TryWrite` calls `MarkDirty()` on the relevant
`FloppyDiskDrive` after every successful write, so games that save to floppy
and re-read the data see consistent in-memory state. Calling `FlushToDisk()`
(e.g. before swapping images or on clean exit) persists the changes to the
host `.img` file.

---

## 8. FloppyDiskDrive and DosDriveManager

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

## 9. CD-ROM image layer

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

## 10. MSCDEX (INT 2Fh AH=15h)

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

## 11. Multi-image disc switching and Ctrl-F4

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

## 12. MOUNT / IMGMOUNT batch commands

Both commands are implemented as batch command handlers in
`DosBatchExecutionEngine.CommandHandlers.cs`.

### MOUNT

```
MOUNT <drive> <path> [-t cdrom|floppy|hdd]
```

- `<path>` may be an **absolute** host path or a **relative** path.
  Relative paths are resolved against `Environment.CurrentDirectory`
  (the directory from which Spice86 was launched) via `Path.GetFullPath`.
- Paths that contain spaces must be surrounded by **double-quotes**:
  `MOUNT D "C:\Program Files\games"`.
- `-t cdrom` → creates an `IsoImage` from an `.iso` or a folder; registers
  a `MscdexDriveEntry`.
- `-t floppy` → calls `DosDriveManager.MountFloppyFolder`.
- `-t hdd` (default) → calls `DosDriveManager.MountFolderDrive`.

### IMGMOUNT

```
IMGMOUNT <drive> <image1> [<image2> …] [-t floppy|iso|cue]
```

- Each image path may be **absolute** or **relative** (resolved via
  `Path.GetFullPath` against the process working directory).
- Paths that contain spaces must be surrounded by **double-quotes**.
- Multiple image paths mount all images to the same drive for disc
  switching; the first image becomes active.
- `-t floppy` → first image → `MountFloppyImage`; further images →
  `AddFloppyImage`.
- `-t iso` → `IsoImage` → `MscdexService.AddDrive`.
- `-t cue` → `CueBinImage` → `MscdexService.AddDrive`.
- Extension auto-detection: `.iso` → iso mode; `.cue` → cue mode;
  `.img` / `.ima` / `.vfd` → floppy mode.

### Argument parsing

Both commands use `SplitArgumentsWithQuotes`, a private static helper in
`DosBatchExecutionEngine.CommandHandlers.cs`, which honours double-quoted
tokens exactly like DOSBox Staging's shell tokeniser:

```
Input: A "C:\my games\disc 1.img" -t floppy
Parts: ["A", "C:\\my games\\disc 1.img", "-t", "floppy"]
```

---

## 13. UI drive-status indicators

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

## 14. Dependency-injection wiring

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

## 15. Test coverage

| Test class                         | Count  | What it covers                                              |
|------------------------------------|--------|-------------------------------------------------------------|
| `Int13FloppyTests`                 | 9      | AH=0x00/01/02/03/08/15; error paths; boundary checks        |
| `BiosParameterBlockTests`          | 8      | Parse from raw bytes; various floppy geometries             |
| `FatDirectoryEntryTests`           | 7      | Attribute flags; deleted/LFN skip; name parsing             |
| `Fat12FileSystemTests`             | 12     | Root dir listing; cluster chain; file read; volume label    |
| `FatFileSystemTests`               | 12     | FAT12/16/32 type detection; cluster chain; volume label     |
| `FloppyDiskControllerTests`        | 10     | SPECIFY/RECALIBRATE/SEEK/READ/WRITE; MSR state machine      |
| `FloppyWriteBackTests`             | 5      | IsDirty; MarkDirty; FlushToDisk round-trip                  |
| `VirtualFloppyImageTests`          | 8      | Build from directory; FAT12 parseable; files readable       |
| `VirtualIsoImageTests`             | 9      | ICdRomImage; PVD; directory records; file data sector reads |
| `DosDriveManagerFloppyTests`       | 14     | Mount/add/swap images; TryGetGeometry; TryRead/TryWrite     |
| `CdRomDriveDiscSwapTests`          | 8      | Single/multi-image; swap cycling; audio stop on swap        |
| `MountBatchCommandTests`           | 12     | MOUNT/IMGMOUNT: absolute paths, relative paths, quoted paths with spaces, multi-image, extension detection |
| `MscdexDeviceDriverRequestTests`   | (existing) | IOCTL sub-commands; audio commands                      |
| `CdAudioPlayerTests`               | (existing) | Play/stop/pause/resume state machine                    |

Full test suite: **1867 tests pass**, 1 skipped (pre-existing), 0 failures.

---

_End of document_
