# Floppy / CD-ROM Subsystem — Architecture Reference

_Spice86 branch: `implement-mscdex-support`_

This document describes the **architecture** of the floppy and CD-ROM subsystem:
the compile-time dependency graph, runtime data flows, interface contracts, and
the design rationale behind each decision. It is written for contributors who
need to understand or modify the subsystem.

---

## Table of Contents

1. [Dependency graph and layer contracts](#1-dependency-graph-and-layer-contracts)
2. [IFloppyDriveAccess — the BIOS/DOS boundary](#2-ifloppydriveaccess--the-biosdos-boundary)
3. [INT 13h handler — sector access and CHS arithmetic](#3-int-13h-handler--sector-access-and-chs-arithmetic)
4. [INT 25h / INT 26h — absolute disk I/O](#4-int-25h--int-26h--absolute-disk-io)
5. [Intel 8272A FDC emulation](#5-intel-8272a-fdc-emulation)
6. [FAT12/16/32 parsing and type detection](#6-fat121632-parsing-and-type-detection)
7. [Floppy image lifecycle: mount, write, flush](#7-floppy-image-lifecycle-mount-write-flush)
8. [Virtual disk images from host directories](#8-virtual-disk-images-from-host-directories)
9. [CD-ROM image layer: IsoImage and CueBinImage](#9-cd-rom-image-layer-isoimage-and-cuebinimage)
10. [MSCDEX (INT 2Fh AH=15h) architecture](#10-mscdex-int-2fh-ah15h-architecture)
11. [Multi-image disc switching and Ctrl-F4](#11-multi-image-disc-switching-and-ctrl-f4)
12. [MOUNT / IMGMOUNT batch commands and path resolution](#12-mount--imgmount-batch-commands-and-path-resolution)
13. [Floppy sound emulation (DOSBox Staging port)](#13-floppy-sound-emulation-dosbox-staging-port)
14. [UI layer: polling, notifications, and drive menu](#14-ui-layer-polling-notifications-and-drive-menu)
15. [Dependency-injection wiring and construction order](#15-dependency-injection-wiring-and-construction-order)
16. [Logging strategy (DOSBox Staging parity)](#16-logging-strategy-dosbox-staging-parity)
17. [Test coverage matrix](#17-test-coverage-matrix)

---

## 1. Dependency graph and layer contracts

### Compile-time dependency graph

```
┌──────────────────────────────────────────────────────────────────────┐
│  Spice86 (UI)                                                        │
│  MainWindowViewModel  DrivesMenuViewModel  DriveStatusViewModel      │
│        │                    │                      │                 │
│        │ IDiscSwapper        │ IDriveStatusProvider  │ IDriveEventNotifier  │
└────────┼────────────────────┼──────────────────────┼─────────────────┘
         │                    │                      │
┌────────▼────────────────────▼──────────────────────▼─────────────────┐
│  Spice86.Core — DOS layer (Spice86.Core.Emulator.OperatingSystem)    │
│  Dos  ←→  DosDriveManager  MscdexService  DosProcessManager          │
│           │  implements                                               │
│           ▼                                                           │
│  IFloppyDriveAccess  (Spice86.Core.Emulator.Devices.Storage)        │
└───────────────────────────────────┬──────────────────────────────────┘
                                    │  consumed by
┌───────────────────────────────────▼──────────────────────────────────┐
│  Spice86.Core — BIOS layer (Spice86.Core.Emulator.InterruptHandlers) │
│  SystemBiosInt13Handler                                               │
│  (zero imports from OperatingSystem namespace)                       │
└───────────────────────────────────┬──────────────────────────────────┘
                                    │  uses
┌───────────────────────────────────▼──────────────────────────────────┐
│  Spice86.Core — Hardware abstraction                                  │
│  FloppyDiskDrive   CdRomDrive   FloppyDiskController (8272A FDC)     │
│  FatFileSystem     CueBinImage  IsoImage  VirtualFloppyImage          │
└──────────────────────────────────────────────────────────────────────┘
```

**Arrows point towards dependencies.** The critical inversion is:
`DosDriveManager` (DOS layer) _implements_ `IFloppyDriveAccess` (hardware
namespace), so the DOS layer depends on the hardware contract, not the other
way around. The BIOS layer (`SystemBiosInt13Handler`) depends only on
`IFloppyDriveAccess` — it never imports any `OperatingSystem` type.

This mirrors DOSBox Staging's design: `bios_disk.cpp` reads from an
`imageDiskList[]` array that DOS fills, operating only on geometry and raw
byte buffers, with no `DOS_Drive` pointers visible to the BIOS code.

### Interface contracts between layers

| Interface | Defined in | Implemented by | Consumed by |
|-----------|------------|----------------|-------------|
| `IFloppyDriveAccess` | `Devices.Storage` | `DosDriveManager` | `SystemBiosInt13Handler`, `DosInt25Handler`, `DosInt26Handler` |
| `IDiscSwapper` | `Spice86.Shared` | `Dos` | `MainWindowViewModel` |
| `IDriveStatusProvider` | `Spice86.Shared` | `Dos` | `DriveStatusViewModel`, `DrivesMenuViewModel` |
| `IDriveEventNotifier` | `Spice86.ViewModels.Services` | `WindowDriveEventNotifier`, `NullDriveEventNotifier` | `DrivesMenuViewModel` |
| `IDriveMountService` | `Spice86.Shared` | `Dos` | `DrivesMenuViewModel` |
| `ICdRomImage` | `Devices.CdRom.Image` | `IsoImage`, `CueBinImage`, `VirtualIsoImage` | `CdRomDrive`, `MscdexService` |

---

## 2. IFloppyDriveAccess — the BIOS/DOS boundary

`IFloppyDriveAccess` is the only channel through which the BIOS layer touches
floppy media. It exposes raw sector access and geometry without any DOS concept
leaking across the boundary.

```
namespace Spice86.Core.Emulator.Devices.Storage;

interface IFloppyDriveAccess {
    bool TryGetGeometry(byte driveNumber,
        out int totalCylinders,
        out int headsPerCylinder,
        out int sectorsPerTrack,
        out int bytesPerSector);

    bool TryRead (byte driveNumber, int imageByteOffset,
        byte[] destination, int destOffset, int byteCount);

    bool TryWrite(byte driveNumber, int imageByteOffset,
        byte[] source,      int srcOffset,  int byteCount);
}
```

`driveNumber` is the BIOS numbering: `0` = A:, `1` = B:, `0x80+` = hard disks
(hard disks are not handled by this interface — callers check the drive number
first).

### DosDriveManager implementation path

```
DosDriveManager.TryGetGeometry(driveNumber, ...)
  1. Map driveNumber → driveLetter  (0→'A', 1→'B')
  2. _floppyDriveMap.TryGetValue(driveLetter, out FloppyDiskDrive fdd)
  3. byte[] raw = fdd.GetCurrentImageData()
  4. BiosParameterBlock bpb = BiosParameterBlock.Parse(raw)
  5. Fill out-params from bpb.SectorsPerTrack, NumberOfHeads, TotalSectors...
  6. totalCylinders = TotalSectors / (heads * sectorsPerTrack)

DosDriveManager.TryRead(driveNumber, imageByteOffset, ...)
  1-2. same drive lookup
  3. Array.Copy(raw, imageByteOffset, destination, destOffset, byteCount)

DosDriveManager.TryWrite(driveNumber, imageByteOffset, ...)
  1-2. same drive lookup
  3. Array.Copy(source, srcOffset, raw, imageByteOffset, byteCount)
  4. fdd.MarkDirty()   ← write-back tracking
```

The in-memory `byte[]` that both read and write operate on is the same array
that `FatFileSystem` traverses. Writes are immediately visible to the FAT
parser — no coherency problem.

---

## 3. INT 13h handler — sector access and CHS arithmetic

`SystemBiosInt13Handler` translates BIOS CHS addresses to flat byte offsets and
delegates all I/O through `IFloppyDriveAccess`.

### CHS → LBA → byte-offset formula

```
// CHS registers from CPU state (BIOS convention, 1-based sector)
cylinder = (CH & 0xFF) | ((CL >> 6) << 8)
head     = DH
sector   = CL & 0x3F                        // 1-based

LBA = (cylinder * headsPerCylinder + head) * sectorsPerTrack + (sector - 1)
byteOffset = LBA * bytesPerSector
```

### Subfunction dispatch table (AH value)

| AH | Name | BIOS action | Implementation |
|----|------|-------------|----------------|
| 0x00 | Reset Disk System | Always succeeds | Clear CF; AH=0 |
| 0x01 | Get Last Drive Status | Per-drive error byte | Return `_lastStatus[driveNumber]` |
| 0x02 | Read Sectors | CHS→offset, `IFloppyDriveAccess.TryRead` | Writes to ES:BX |
| 0x03 | Write Sectors | CHS→offset, `IFloppyDriveAccess.TryWrite` | Reads from ES:BX |
| 0x04 | Verify Sectors | Stub | Succeeds when AL > 0 |
| 0x05 | Format Track | Zero all sectors in CHS track | `TryWrite` each sector with zeroes |
| 0x08 | Get Drive Parameters | BPB geometry + BL=0x04 (1.44 MB) | `TryGetGeometry`; CX/DX encoding |
| 0x0C | Seek | Cylinder seek | Verify ready; play seek sound |
| 0x0D | Reset HDD Controller | Same as AH=0x00 | Delegates to Reset |
| 0x10 | Test Drive Ready | Check for media | `TryGetGeometry`; AH=0 on success |
| 0x11 | Recalibrate | Head-0 seek | Play seek sound; always succeeds |
| 0x15 | Get Drive Type | AH=0x02 (floppy) / 0x03 (HDD ≥ 0x80) | Drive-number range check |
| 0x16 | Change Line Status | Disc-change detection | No-change for mounted; error if not |
| 0x17 | Set DASD Type for Format | Stub | Always succeeds |
| 0x18 | Set Media Type for Format | Stub | Always succeeds |

### Error propagation

On failure, `SystemBiosInt13Handler`:
1. Sets `AH` to the BIOS error code (e.g. `0x80` = drive not ready).
2. Sets Carry Flag (CF = 1).
3. Stores the same code in `_lastStatus[driveNumber]` (returned by AH=0x01).

On success: `AH = 0`, CF = 0, `_lastStatus[driveNumber] = 0`.

---

## 4. INT 25h / INT 26h — absolute disk I/O

INT 25h (Absolute Disk Read) and INT 26h (Absolute Disk Write) provide a
DOS-level interface to raw sectors, bypassing the FAT file system.

### Standard mode (CX ≠ 0xFFFF)

```
AL = drive (0=A:, 1=B:, 2=C:, ...)
CX = sector count
DX = starting logical sector (LBA)
DS:BX = transfer buffer
```

### Extended mode (CX = 0xFFFF, DOS 3.31+)

```
AL = drive
DS:BX → { DWORD start_sector; WORD sector_count; DWORD far_ptr_buffer }
```

Both handlers implement dual-mode dispatch:

```
DosInt25Handler.Run()
  1. if (AL > 1) → HDD stub (return success)
  2. if (CX == 0xFFFF) → read extended packet from DS:BX
     else              → use CX/DX directly
  3. byteOffset = startSector * 512
  4. _floppyAccess.TryRead(AL, byteOffset, buffer, ...)
  5. CF=0 on success; CF=1 + error code on failure

DosInt26Handler.Run()
  same structure, but calls TryWrite and marks image dirty
```

---

## 5. Intel 8272A FDC emulation

`FloppyDiskController` emulates the Intel 82077AA (NEC µPD765) floppy disk
controller chip, which is the hardware interface between the CPU and the physical
floppy drive mechanism.

### I/O port map

| Port | R/W | Register | Function |
|------|-----|----------|----------|
| 0x3F2 | W | Digital Output Register | Motor control (bits 4-7), drive select (bits 0-1), reset (bit 2), DMA enable (bit 3) |
| 0x3F4 | R | Main Status Register | Controller readiness flags |
| 0x3F5 | R/W | Data FIFO | Command bytes in; result bytes out |

### Main Status Register bit encoding

```
Bit 7: MRQ  (1 = data register ready for transfer)
Bit 6: DIO  (1 = FDC→CPU; 0 = CPU→FDC)
Bit 5: NDM  (1 = non-DMA mode)
Bit 4: CB   (1 = command in progress)
Bit 3-0: FDD busy flags (drives 3-0 seeking)
```

### Command state machine

The FDC protocol is phase-driven: command bytes arrive via sequential writes to
port 0x3F5, then the controller executes, then result bytes are read back.

```
IDLE ──write─→ COMMAND_RECEIVE ──execute─→ RESULT_READY ──read─→ IDLE
```

State transitions for READ DATA (command byte 0xE6):

```
State: IDLE
  CPU writes 0xE6 → COMMAND_RECEIVE  (expecting 8 more bytes)
  CPU writes MT, MFM, SK flags (byte 1)
  CPU writes HD/DS (byte 2)
  CPU writes C, H, R, N, EOT, GPL, DTL (bytes 3-8)
State: COMMAND_RECEIVE → EXECUTE
  FDC computes sector LBA from C/H/R/N
  DMA channel 2 transfers sector bytes to RAM
  IRQ 6 raised
State: RESULT_READY
  CPU reads ST0, ST1, ST2, C, H, R, N (7 result bytes)
State: IDLE
```

### Supported command set

| Command byte | Mnemonic | Phases | Notes |
|---|---|---|---|
| 0x03 | SPECIFY | 2 cmd / 0 result | Sets step rate, head load/unload times |
| 0x04 | SENSE DRIVE STATUS | 1 cmd / 1 result | Returns ST3 |
| 0x07 | RECALIBRATE | 1 cmd / 0 result + IRQ6 | Seek to track 0 |
| 0x08 | SENSE INTERRUPT STATUS | 0 cmd / 2 result | Clears interrupt, returns ST0+PCN |
| 0x0F | SEEK | 2 cmd / 0 result + IRQ6 | Seek to cylinder |
| 0xE6 | READ DATA | 8 cmd / 7 result + IRQ6 + DMA | Data transfer via DMA ch.2 |
| 0xC5 | WRITE DATA | 8 cmd / 7 result + IRQ6 + DMA | Data transfer via DMA ch.2 |
| 0x4A | READ ID | 1 cmd / 7 result + IRQ6 | Returns C/H/R/N of current position |
| 0x4D | FORMAT TRACK | 5 cmd / 7 result + IRQ6 | Writes format patterns |

### DMA and IRQ integration

```
FloppyDiskController
├── DmaChannel channel2   ← injected from DmaBus
│     READ DATA:  FDC reads sector bytes → writes to DMA transfer buffer
│     WRITE DATA: FDC reads from DMA buffer → writes sector bytes
└── Action<byte> _raiseIrq6  ← delegate to Intel8259Pic
      called at end of: RECALIBRATE, SEEK, READ DATA, WRITE DATA, READ ID,
                        FORMAT TRACK
```

The 8272A FDC and DMA channel 2 form a hardware data path that the CPU
programs but does not execute directly, matching the behaviour seen in
real DOS programs that issue FDC commands and then wait for IRQ 6.

---

## 6. FAT12/16/32 parsing and type detection

### Class hierarchy

```
Spice86.Core.Emulator.OperatingSystem.FileSystem
├── BiosParameterBlock          ← parse boot sector, detect FAT type
├── FatType                     ← enum { Fat12, Fat16, Fat32 }
├── FatDirectoryEntry           ← parse 32-byte directory records
├── FatFileSystem               ← unified traversal for all three FAT variants
└── Fat12FileSystem             ← backward-compatible type alias (= FatFileSystem)
```

### BiosParameterBlock boot sector layout

```
Byte offset  Width  Field
0x00         3      JmpBoot + OEM name (0x0B onwards is the BPB proper)
0x0B         2      BytesPerSector         (normally 512)
0x0D         1      SectorsPerCluster      (power of 2, 1..128)
0x0E         2      ReservedSectorCount    (sectors before FAT region)
0x10         1      NumberOfFats           (normally 2)
0x11         2      RootEntryCount         (max root-dir entries; 0 for FAT32)
0x13         2      TotalSectors16         (0 when > 65535 sectors)
0x15         1      MediaDescriptor        (0xF0 = removable)
0x16         2      SectorsPerFat16        (0 for FAT32)
0x18         2      SectorsPerTrack        (geometry, used by INT 13h)
0x1A         2      NumberOfHeads          (geometry, used by INT 13h)
0x1C         4      HiddenSectors
0x20         4      TotalSectors32         (used when TotalSectors16 == 0)
── FAT32 extended BPB (present when SectorsPerFat16 == 0) ──────────────
0x24         4      SectorsPerFat32
0x2C         4      RootCluster            (root dir cluster, normally 2)
```

### FAT type detection algorithm (Microsoft specification)

The FAT variant is determined **solely** from the cluster count, not from the
`FileSystemType` string in the BPB (which is informational only):

```csharp
int rootDirSectors = (RootEntryCount * 32 + BytesPerSector - 1) / BytesPerSector;
int fatSize   = SectorsPerFat16 != 0 ? SectorsPerFat16 : SectorsPerFat32;
int totalSec  = TotalSectors16  != 0 ? TotalSectors16  : TotalSectors32;
int dataSec   = totalSec - ReservedSectorCount - NumberOfFats * fatSize - rootDirSectors;
int clusterCount = dataSec / SectorsPerCluster;

FatType = clusterCount < 4085  ? FatType.Fat12
        : clusterCount < 65525 ? FatType.Fat16
        :                        FatType.Fat32;
```

### FAT entry width and cluster-chain reading

```
FAT12: read 12-bit entries (packed pairs, byte-boundary handling required)
       offset = (clusterNumber * 3) / 2      // 1.5 bytes per entry
       entry  = (clusterNumber % 2 == 0)
              ? (fat[offset]     | (fat[offset + 1] << 8)) & 0x0FFF
              : (fat[offset] >> 4 | (fat[offset + 1] << 4)) & 0x0FFF
       EOC sentinel: value >= 0xFF8

FAT16: read 16-bit LE values; EOC >= 0xFFF8
FAT32: read 32-bit LE values masked to 28 bits; EOC >= 0x0FFFFFF8
```

### Root directory region

```
FAT12/16: fixed region after FATs
           rootDirOffset = (ReservedSectorCount + NumberOfFats * fatSize) * BytesPerSector
           rootDirSize   = RootEntryCount * 32 bytes

FAT32:     root dir is a normal cluster chain starting at RootCluster
           traversed identically to any other directory
```

### FatDirectoryEntry parsing

Each entry is 32 bytes. The parser skips:
- entries where `Name[0] == 0xE5` (deleted)
- entries where `Name[0] == 0x00` (end of directory)
- entries where `Attributes == 0x0F` (Long File Name entry)

---

## 7. Floppy image lifecycle: mount, write, flush

### State transitions in FloppyDiskDrive

```
Empty drive (no images)
  │
  ├─MountImage(data, path)──→  Single-image mode  (Image = FatFileSystem(data))
  │                                    │
  │                                    ├─AddImage(data2, path2)──→  Multi-image mode
  │                                    │     _images = [(data,path),(data2,path2)]
  │                                    │     _currentIndex = 0
  │                                    │
  │                                    └─SwapToNextImage()──→  next image active
  │                                          _currentIndex++
  │                                          Image rebuilt from new data
  │
  └─MountFloppyFolder(path)──→  Folder mode (Image = null, no raw sector access)
```

### Write-back mechanism

```
Guest writes a sector:
  DosDriveManager.TryWrite(driveNumber, byteOffset, src, ...)
    ├── Array.Copy into fdd.GetCurrentImageData()  (in-place mutation)
    └── fdd.MarkDirty()                            (set IsDirty = true)

Before disc swap / clean exit:
  fdd.FlushToDisk()
    ├── if (!IsDirty) return
    ├── File.WriteAllBytes(ImagePath, GetCurrentImageData())
    └── IsDirty = false
```

Writes are immediately visible to the in-memory `FatFileSystem` because both
the INT 13h path and the FAT parser operate on the same `byte[]` reference.

---

## 8. Virtual disk images from host directories

Both `VirtualFloppyImage` and `VirtualIsoImage` construct in-memory disk images
from host file system trees, enabling disc switching for folder mounts.

### VirtualFloppyImage — 1.44 MB FAT12 builder

```
VirtualFloppyImage.Build(hostDirectory, logger) : byte[1,474,560]

Memory layout:
  Sector 0          Boot sector / BPB
                      BytesPerSector = 512
                      SectorsPerCluster = 1
                      ReservedSectorCount = 1
                      NumberOfFats = 2
                      RootEntryCount = 224
                      TotalSectors16 = 2880
                      MediaDescriptor = 0xF0
                      SectorsPerFat = 9
                      SectorsPerTrack = 18
                      NumberOfHeads = 2

  Sectors 1-9       FAT1 (9 sectors, 0xF0 0xFF 0xFF at start)
  Sectors 10-18     FAT2 (mirror of FAT1)
  Sectors 19-32     Root directory (14 sectors, 224 × 32-byte entries)
  Sectors 33-2879   Data clusters 2-2848 (files and subdirectory data)

Algorithm:
  1. Allocate FAT chain for each file in source directory (and one subdirectory level)
  2. Write 32-byte directory entries for each file (name, size, first cluster)
  3. Pack file data into consecutive clusters in the data area
  4. Files that would overflow available clusters are skipped with a warning log
```

### VirtualIsoImage — ISO 9660 builder

```
VirtualIsoImage(hostDirectory, volumeLabel) : ICdRomImage

Memory layout (2048-byte sectors):
  Sector 0-15       System area (zeroed)
  Sector 16         Primary Volume Descriptor
                      TypeCode = 0x01
                      StandardIdentifier = "CD001"
                      VolumeSpaceSize = total_sector_count
                      LogicalBlockSize = 2048
                      RootDirectoryRecord pointing to sector 20
  Sector 17         Volume Descriptor Set Terminator (TypeCode = 0xFF)
  Sector 18         Path Table (LE byte order)
  Sector 19         Path Table (BE byte order)
  Sector 20         Root directory records (2048 bytes, all files)
  Sectors 21+       File data (one contiguous region per file)

ISO 9660 Directory Record structure (variable length):
  Byte  0: LEN_DR (record length, including padding to even boundary)
  Byte  1: Extended attribute record length (0)
  Bytes 2-9:  Location of Extent (LE DWORD + BE DWORD)
  Bytes 10-17: Data Length (LE DWORD + BE DWORD)
  Bytes 18-24: Recording Date and Time (7 bytes)
  Byte 25: File Flags (0x02 = directory, 0x00 = file)
  Byte 32+: File Identifier (ASCII, no version number for simplicity)
```

---

## 9. CD-ROM image layer: IsoImage and CueBinImage

### ICdRomImage contract

```
interface ICdRomImage {
    IReadOnlyList<CdTrack> Tracks { get; }
    int TotalSectors { get; }
    PrimaryVolumeDescriptor? PrimaryVolume { get; }
    void Read(byte[] buffer, int lba, int sectorCount);
    void Dispose();
}
```

### IsoImage

`IsoImage` wraps a flat `.iso` file (Mode 1, 2048 bytes/sector). All reads are
`File.Read` at offset `lba * 2048`. `PrimaryVolume` is parsed from sector 16.

### CueBinImage — multi-track BIN/CUE

```
CUE parsing pipeline:
  CueSheetParser.Parse(cueFile) → CueSheet → List<CueEntry>
    CueEntry: {FileName, TrackNumber, TrackMode, IndexNumber, Pregap, Postgap, MSF}

CueBinImage.BuildTracks(sheet):
  1. Group CueEntries by TrackNumber (dictionary)
  2. For each track group (sorted):
       a. Entries[0]: file, mode, pregap/postgap
       b. FirstOrDefault(e => e.IndexNumber == 1): start MSF → LBA
  3. Track length:
       nextTrack.StartLba - thisTrack.StartLba (for intermediate tracks)
       (binFileSize / sectorSize) - thisTrack.StartLba (for last track)
  4. FileOffset = StartLba * SectorSize (per-track, handles multi-file CUEs)
```

### Sector framing modes

| CdSectorMode | Bytes/sector | Header | Data region |
|---|---|---|---|
| `CookedData2048` | 2048 | none | entire sector |
| `Raw2352` | 2352 | 16-byte sync + header | bytes 16-2063 |
| `Mode2Form1` | 2352 | 24-byte XA header | bytes 24-2071 |

`CueBinImage.Read` extracts only the 2048-byte data payload from raw/Mode 2
sectors, presenting a uniform interface to callers regardless of physical
sector format.

### Path resolution for multi-file CUEs

`ParseFileName` uses `Path.GetFullPath(rawBinName, cueDirectory)` rather than
`Path.Combine`. This is important: `Path.Combine` silently drops the base
directory when `rawBinName` starts with a drive letter or path separator,
which breaks multi-file CUEs where BIN filenames may be absolute.

---

## 10. MSCDEX (INT 2Fh AH=15h) architecture

### Ownership and construction

```
Dos constructor
  └── _mscdexService = new MscdexService(state, memory, loggerService)
        ↓ stored in Dos (never passed to DI from outside)
  ↓ passed to:
  DosProcessManager(mscdex, ...) → DosBatchExecutionEngine (IMGMOUNT cmd handler)
  DosInt2fHandler.Dispatch() calls _mscdexService.Dispatch()
```

`MscdexService` is constructed _before_ `ProcessManager` so it is always
present when INT 2Fh fires. This matches DOSBox Staging, where MSCDEX is
installed at DOS boot time, independent of what programs are run.

### Drive registry

```
MscdexService
├── List<MscdexDriveEntry> _drives
│     MscdexDriveEntry { ICdRomDrive Drive, byte DriveIndex, char DriveLetter }
│
├── AddDrive(letter, drive)      → append + log
├── TryGetDrive(index, out drv)  → _drives[index] (O(1))
├── TryGetDriveByLetter(ch, ...) → FirstOrDefault(d => d.DriveLetter == ch)
└── TryGetDriveBySubUnit(n, ...) → _drives[n] (same as index)
```

### Implemented IOCTL sub-commands (AL=0x10 SendDeviceDriverRequest)

```
IOCTL Input commands:
  0x00  Device status        → status DWORD (bit 11 = door open, bit 0 = door closed)
  0x01  Sector size          → 2048 (always)
  0x02  Volume size          → ICdRomImage.TotalSectors
  0x06  Device status (alt)  → same as 0x00
  0x09  Media changed        → ICdRomDrive.MediaState.HasChanged
  0x0B  Audio channel info   → L/R volume mapping

Audio control commands:
  0x80  Stop audio           → ICdRomDrive.StopAudio()
  0x81  Resume audio         → ICdRomDrive.ResumeAudio()
  0x84  Play audio (MSF/LBA) → ICdRomDrive.PlayAudio(start, end)
```

---

## 11. Multi-image disc switching and Ctrl-F4

### Data structures

```
FloppyDiskDrive
├── List<(byte[] Data, string Path)> _images  ← in order of addition
├── int _currentIndex                          ← wraps around on swap
└── SwapToNextImage()
      _currentIndex = (_currentIndex + 1) % _images.Count
      ApplyCurrentImage()  → rebuild FatFileSystem, update Label

CdRomDrive
├── List<ICdRomImage> _images
├── int _currentIndex
└── SwapToNextDisc()
      MediaState.IsDoorOpen = true           ← signals media change to MSCDEX
      _currentIndex = (_currentIndex + 1) % _images.Count
      _image = _images[_currentIndex]
      MediaState.IsDoorOpen = false          ← re-insert
      StopAudio()
```

### Control flow for Ctrl-F4

```
MainWindowViewModel.OnKeyDown(e)
  ├── if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.Control)
  │     _discSwapper.SwapDiscImages()         ← does NOT forward to emulator
  │     e.Handled = true
  │     return
  └── else → emit keyboard event to emulated CPU

Dos.SwapDiscImages()   (implements IDiscSwapper)
  ├── foreach (MscdexDriveEntry e in _mscdexService.Drives)
  │     e.Drive.SwapToNextDisc()
  └── _driveManager.SwapFloppyDiscs()
        └── foreach (FloppyDiskDrive fdd in _floppyDriveMap.Values)
              fdd.SwapToNextImage()
```

### MSCDEX media-change detection after disc swap

When a guest program calls `IOCTL 0x09 MediaChanged`, `MscdexService` queries
`ICdRomDrive.MediaState.HasChanged`. `SwapToNextDisc` sets `IsDoorOpen = true`
then `false`, which the `MediaState` object translates into `HasChanged = true`.
The next `MediaChanged` query returns `0xFF` (changed) and resets the flag.

---

## 12. MOUNT / IMGMOUNT batch commands and path resolution

### BatchArgumentParser.SplitWithQuotes

All command argument strings are tokenised by `BatchArgumentParser.SplitWithQuotes`,
which handles embedded spaces inside double-quoted tokens — identical to DOSBox
Staging's shell tokeniser:

```
Input:  A "C:\my games\disc 1.img" -t floppy
Output: ["A", @"C:\my games\disc 1.img", "-t", "floppy"]
```

### HostPathResolver.Resolve — four-priority chain

```
HostPathResolver.Resolve(rawToken, driveManager)

Priority 1: DOS drive letter prefix
  if rawToken matches ^([A-Za-z]):[\\/](.*)$
    letter = group[1].ToUpperInvariant()
    relativePart = group[2]
    driveManager.TryGetValue(letter, out VirtualDrive vd)
    → Path.GetFullPath(relativePart, vd.MountedHostDirectory)

Priority 2: Absolute host path
  if Path.IsPathRooted(rawToken)
    → Path.GetFullPath(rawToken)

Priority 3: Relative path against current DOS directory
  currentDrive = driveManager.CurrentDrive
  hostBase = currentDrive.MountedHostDirectory
  dosDir   = currentDrive.CurrentDosDirectory
  → Path.GetFullPath(rawToken, Path.Combine(hostBase, dosDir))

Priority 4: Fallback (process working directory)
  → Path.GetFullPath(rawToken)
```

`Path.GetFullPath` exceptions (`ArgumentException`, `NotSupportedException`,
`PathTooLongException`) are caught and reported as error log messages with the
raw token included.

### MOUNT command dispatch

```
MOUNT <drive> <path> [-t cdrom|floppy|hdd]

Path resolution: HostPathResolver.Resolve(path, driveManager)
-t cdrom  → DosDriveManager.MountFolderAsCdRom(letter, hostPath)
             MscdexService.AddDrive(letter, new CdRomDrive(new VirtualIsoImage(hostPath)))
-t floppy → DosDriveManager.MountFloppyFolder(letter, hostPath)
-t hdd    → DosDriveManager.MountFolderDrive(letter, hostPath)
(default)   same as -t hdd
```

### IMGMOUNT command dispatch

```
IMGMOUNT <drive> <image1> [<image2>…] [-t floppy|iso|cue]

Each image path resolved via HostPathResolver.Resolve
-t floppy:
  first image → DosDriveManager.MountFloppyImage(letter, data, path)
  subsequent  → DosDriveManager.AddFloppyImage(letter, data, path)
-t iso:
  for each image → MscdexService.AddDrive(letter, new CdRomDrive(new IsoImage(path)))
-t cue:
  for each image → MscdexService.AddDrive(letter, new CdRomDrive(new CueBinImage(path)))
auto-detect (no -t):
  .iso → iso; .cue → cue; .img/.ima/.vfd → floppy
```

---

## 13. Floppy sound emulation (DOSBox Staging port)

`FloppyDiskNoiseDevice` is a faithful port of DOSBox Staging's `DiskNoiseDevice`
class. The state machine and constants are intentionally matched to staging to
produce identical audio behaviour.

### State machine

```
States: Off | SpinUp | SpinSustain | Seeking

Off ──NotifyAccess()──→ SpinUp
  Plays fdd_spinup.wav (one-shot)
  After spinup duration → SpinSustain

SpinSustain
  Plays fdd_spin.wav (non-looping for floppy, unlike HDD which loops)
  On each NotifyAccess() → Seeking (seek sound interrupts sustain)

Seeking (triggered by SetLastIoPath / NotifyAccess)
  Sequential access:  ChooseSeekIndex returns index 0 or 1 (80% probability)
  Random access:      ChooseSeekIndex returns index 2..8 (20% probability)
  After seek sample completes → SpinSustain (or Off if idle)
```

### Sample weighting (ChooseSeekIndex)

```csharp
// Matches DOSBox Staging's ChooseSeekIndex() exactly
int ChooseSeekIndex(FloppySeekType seekType) {
    if (seekType == FloppySeekType.Sequential && _random.Next(100) < 80)
        return _random.Next(2);        // first two samples: frequent short seeks
    return 2 + _random.Next(7);       // remaining 7 samples: long/varied seeks
}
```

### FloppySoundEmulator — sample path resolution

```
Priority 1: Environment.GetFolderPath(SpecialFolder.UserProfile)/disk_noises/
Priority 2: <process directory>/resources/disk_noises/
Priority 3: process working directory

Sample files (WAV, 22050 Hz, mono, 16-bit PCM):
  fdd_spinup.wav   fdd_spin.wav
  fdd_seek1.wav  fdd_seek2.wav  fdd_seek3.wav  fdd_seek4.wav  fdd_seek5.wav
  fdd_seek6.wav  fdd_seek7.wav  fdd_seek8.wav  fdd_seek9.wav
```

`WavPcmLoader` validates the WAV header (RIFF, fmt chunk, PCM, mono,
22050 Hz, 16-bit) and rejects stereo or non-PCM files silently, returning an
empty array so the emulator continues without sound.

---

## 14. UI layer: polling, notifications, and drive menu

### Polling architecture

There are no events or push notifications from the emulator thread to the UI.
All UI updates are driven by a polling timer in the UI thread:

```
DrivesMenuViewModel
├── DispatcherTimer (1 s, Background priority)
└── OnTimerTick() → Refresh()
      IDriveStatusProvider.GetDriveStatuses()    ← cross-thread call into Dos
        → snapshot of DosVirtualDriveStatus objects
      NotifyIfChanged(status) per drive
        → compare status.CurrentImagePath with _lastKnownImagePath[letter]
        → if changed: IDriveEventNotifier.Notify(title, message)
      AllDrives collection updated in-place (letter-keyed)
```

### IDriveEventNotifier — toast notification architecture

```
IDriveEventNotifier  (interface)
├── NullDriveEventNotifier   ← no-op; used before window is ready
└── WindowDriveEventNotifier ← backed by Avalonia WindowNotificationManager
      WindowNotificationManager(MainWindow, NotificationPosition.BottomRight)
      MaxItems = 3

Initialisation sequence:
  DrivesMenuViewModel created with NullDriveEventNotifier
  MainWindow.Loaded fires
  App.axaml.cs creates WindowDriveEventNotifier(window)
  DrivesMenuViewModel.AttachNotifier(notifier) called
  _lastKnownImagePath cleared (prevents spurious toasts for pre-existing drives)
```

Toast content:
- **Mount**: `"Drive A: mounted"` / `"DISK1 — disk1.img"`
- **Disc swap**: `"Drive A: disc swapped"` / `"DISK2 — disk2.img"`
- **Eject**: `"Drive A: ejected"` / `"No media in drive A:"`

### DrivesMenuViewModel — AllDrives collection structure

```
AllDrives : ObservableCollection<DriveMenuItemViewModel>
  DriveMenuItemViewModel
    ├── DriveLetter       char
    ├── DriveType         DosVirtualDriveType
    ├── IsHdd             bool  (= DriveType == Fixed)
    ├── IsMediaPresent    bool
    ├── CurrentImagePath  string
    ├── AllImages         ObservableCollection<string>
    ├── SelectedImage     string?  (bound to ComboBox)
    ├── MountFolderCommand
    └── MountImageCommand
```

The `AllDrives` collection always includes A:, B: (floppy) and D: (CD-ROM
placeholder), plus all mounted drives of any type — so even before mounting,
the UI exposes the correct controls for each slot.

### Drive menu rows

| Drive type | Icon | ComboBox | Mount Folder | Mount Image | ComboBox enabled |
|---|---|---|---|---|---|
| Floppy (A:, B:) | 💾 Save | ✅ | ✅ | ✅ | Yes |
| CD-ROM | ⏏ ArrowEject | ✅ | ✅ | ✅ | Yes |
| HDD (C: etc.) | 🗄 Storage | ✅ | Hidden | Hidden | **No** |

---

## 15. Dependency-injection wiring and construction order

`Spice86DependencyInjection` is the single composition root. The ordering
constraint for the floppy subsystem is:

```
1. Memory, CPU state, I/O bus  (unchanged from before this PR)
2. DmaBus, Intel8259Pic        (required by FDC)
3. FloppyDiskController(state, memory, dmaBus, raiseIrq6)
4. Dos(memory, state, ..., cDrivePath, execPath)
     └─ DosDriveManager created inside Dos
     └─ MscdexService created inside Dos
5. SystemBiosInt13Handler(memory, stack, state,
       floppyAccess: dos.DosDriveManager,    ← cast to IFloppyDriveAccess
       floppySound:  new FloppySoundEmulator(logger))
6. DosInt25Handler(state, memory, dos.DosDriveManager)
7. DosInt26Handler(state, memory, dos.DosDriveManager)
8. interruptInstaller.Install(int13Handler, int25Handler, int26Handler)
9. MainWindowViewModel(
       discSwapper: dos,                      ← IDiscSwapper
       driveStatus: dos,                      ← IDriveStatusProvider
       mountService: dos)                     ← IDriveMountService
10. DrivesMenuViewModel(dos, dos, dos, hostStorage,
        new NullDriveEventNotifier())
11. MainWindow.Loaded → AttachNotifier(new WindowDriveEventNotifier(window))
```

The critical constraint: step 5 must follow step 4, because
`SystemBiosInt13Handler` receives `DosDriveManager` as `IFloppyDriveAccess`.
The BIOS handler never sees the `DosDriveManager` concrete type.

---

## 16. Logging strategy (DOSBox Staging parity)

All drive-subsystem log messages follow the same prefix/format as DOSBox
Staging's `dos_programs.cpp` and `bios_disk.cpp`:

| Event | Level | Message format |
|---|---|---|
| Folder drive mounted | Information | `MOUNT: Drive {Drive}: is now backed by folder {Path}` |
| Floppy image mounted | Information | `IMGMOUNT: Mounted image {Image} on drive {Drive}:` |
| Floppy image added | Information | `IMGMOUNT: Added image {Image} to drive {Drive}: ({Count} total)` |
| Floppy disc swap | Information | `MOUNT: Swapping drive {Drive}: to image {Image}` |
| CD-ROM image mounted | Information | `IMGMOUNT: Mounted CD-ROM image {Image} on drive {Drive}:` |
| CD-ROM disc swap | Information | `MOUNT: Swapping CD-ROM drive {Drive}: to image {Image}` |
| MSCDEX drive registered | Information | `MSCDEX: Registered drive {Drive}: (index {Index})` |
| INT 13h read | Debug | (only at Debug level; not emitted at Info) |
| FDC command | Verbose | (only at Verbose level) |

`ILoggerService.IsEnabled(level)` is always checked before constructing
expensive interpolated strings — matching the existing project convention.

---

## 17. Test coverage matrix

| Test class | Count | What it validates |
|---|---|---|
| `Int13FloppyTests` | 9 | AH subfunctions 0x00/01/02/03/04/05/08/0C/10/11/15/16/17/18; CHS arithmetic; error paths |
| `BiosParameterBlockTests` | 8 | Boot sector parse; geometry fields; FAT32 extended BPB |
| `FatDirectoryEntryTests` | 7 | Attribute flags; deleted/LFN skip; volume label recognition |
| `Fat12FileSystemTests` | 12 | Root dir listing; cluster chain; file read; volume label round-trip |
| `FatFileSystemTests` | 12 | FAT12/16/32 type detection; cluster-chain traversal; FAT32 root dir |
| `FloppyDiskControllerTests` | 10 | SPECIFY; RECALIBRATE; SEEK; READ DATA; WRITE DATA; MSR state machine; IRQ6 |
| `FloppyWriteBackTests` | 5 | IsDirty; MarkDirty; FlushToDisk; round-trip sector write → read |
| `VirtualFloppyImageTests` | 8 | Build from directory; FAT12 parseable; volume label; file data readable |
| `VirtualIsoImageTests` | 9 | ICdRomImage contract; PVD at sector 16; directory records; file sector reads |
| `DosDriveManagerFloppyTests` | 14 | Mount/add/swap; TryGetGeometry; TryRead; TryWrite; dirty flag |
| `CdRomDriveDiscSwapTests` | 8 | Single/multi-image; swap cycling; MediaState; audio stop on swap |
| `MountBatchCommandTests` | 16 | MOUNT/IMGMOUNT; absolute/relative/DOS-drive paths; quoted spaces; multi-image; extension auto-detect |
| `FloppyDiskNoiseDeviceTests` | 9 | State machine; seek-type weighting; spinup/sustain transitions; WavPcmLoader validation |
| `DrivesMenuViewModelTests` | 2 | Toast notification on initial mount; toast on disc swap |

**Total: 1916 tests pass, 0 failures.**

---

_End of document_
