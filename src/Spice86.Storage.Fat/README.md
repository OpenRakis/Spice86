# Spice86.Storage.Fat

Read/write FAT12, FAT16 and FAT32 filesystems, MBR partition tables and floppy
boot sectors from .NET, without any native dependency.

This assembly is shipped as a standalone NuGet package and is intentionally
decoupled from the rest of Spice86: it depends only on `Spice86.Shared` (for a
few plain enums and value types) and on `Serilog` (for the `ILogger` warning
sink used by the floppy image builder). It has no I/O backend assumptions
beyond `byte[]` / `Stream` and the standard `System.IO` file APIs.

---

## Package contents

Namespace root: `Spice86.Shared.Emulator.Storage.FileSystem`.

| Area                | Key types                                                                                  |
|---------------------|---------------------------------------------------------------------------------------------|
| FAT variant id      | `FatType` (Fat12 / Fat16 / Fat32)                                                          |
| Boot sector / BPB   | `FatBiosParameterBlock`, `MutableBiosParameterBlock`, `FatBootSectorCodec`, `FatBootSectorValidator`, `BpbValidationIssue`, `BpbValidationSeverity` |
| Cluster chain       | `FatClusterCodec`, `FatClusterValidator`, `FatTable`                                       |
| Directory entries   | `FatDirectoryEntry`, `MutableFatDirectoryEntry`, `FatDirectoryEntryCodec`, `DirectoryWriter`, `DosNameConverter` |
| Long file names     | `LongFileNameCodec`, `VfatDirectoryReader`, `VfatDirectoryRecord`, `VfatLfnEntry`          |
| Allocation policy   | `FileAllocationStrategy`, `FirstFitAllocationStrategy`, `ContiguousAllocationStrategy`     |
| Filesystem views    | `FatFileSystem`, `Fat12FileSystem`, `MutableFatFileSystem`, `FatFileSystemWriter`          |
| Partitioned images  | `MasterBootRecord`, `MbrCodec`, `PartitionTableEntry`, `PartitionTableValidator`, `PartitionValidationIssue`, `PartitionValidationSeverity`, `FatDiskImage`, `FatFileSystemWithPartition` |
| Image lifecycle     | `FileBackedFatImage` (open / mutate / write-back on `Flush` or `Dispose`)                  |
| Image builders      | `VirtualFloppyImage` (build a 1.44 MB FAT12 floppy image from a host directory)            |

---

## FAT variants supported

The assembly implements the three classical FAT variants with the following
guarantees:

### FAT12

- BPB parsing (`FatBiosParameterBlock.Parse`) and serialisation
  (`MutableBiosParameterBlock.Serialize` with `FatType.Fat12`) including the
  extended boot signature 0x29 and 11-byte ASCII volume label at offset 43.
- 12-bit packed cluster entries, with correct nibble handling on odd-indexed
  entries (`FatClusterCodec` with `FatType.Fat12`).
- End-of-chain marker 0xFF8..0xFFF and bad-cluster marker 0xFF7
  (`FatClusterCodec.Fat12BadCluster`).
- Standard root-directory area (fixed location, fixed
  `BPB.RootDirEntries * 32` bytes).
- Full read/write/commit through `MutableFatFileSystem`.
- 1.44 MB floppy image construction from a host directory via
  `VirtualFloppyImage` (boot sector + BPB + 2 FATs + root directory + data
  area + 0x55 0xAA signature).

### FAT16

- BPB parsing and serialisation (`MutableBiosParameterBlock.Serialize` with
  `FatType.Fat16`, file-system identifier `"FAT16   "`).
- 16-bit little-endian cluster entries (`FatClusterCodec` with
  `FatType.Fat16`).
- End-of-chain marker 0xFFF8..0xFFFF and bad-cluster marker 0xFFF7
  (`FatClusterCodec.Fat16BadCluster`).
- Standard root-directory area.
- Read/write through `MutableFatFileSystem` (the same code paths are used as
  for FAT12; the cluster width is selected by `FatType`).

### FAT32

- BPB parsing and serialisation, including the FAT32-only fields at offsets
  36 (`SectorsPerFat32`), 44 (`RootCluster`) and the file-system identifier
  `"FAT32   "` at offset 82. Requires the 90-byte extended BPB.
- 28-bit cluster entries with the top 4 bits preserved as reserved
  (`FatClusterCodec.Fat32ValueMask = 0x0FFFFFFF`).
- End-of-chain marker 0x0FFFFFF8..0x0FFFFFFF and bad-cluster marker
  0x0FFFFFF7 (`FatClusterCodec.Fat32BadCluster`).
- Root directory accessed as a cluster chain starting at `BPB.RootCluster`
  (no fixed-size root area).
- Read/write through `MutableFatFileSystem` using the FAT32 cluster codec.

### Boot sector consistency

`FatBootSectorValidator.ValidateBpbConsistency(bpb, fatType)` returns a list
of `BpbValidationIssue` values with `Info`, `Warning` or `Error` severity. The
validator checks the cluster-count-derived FAT type, sector-size sanity,
reserved-sector counts, FAT count, root-entry alignment and FAT32-specific
constraints. It is a pure function and does not require an image to be
mounted.

---

## Long file names (VFAT)

The VFAT LFN extension is implemented for read access:

- `VfatDirectoryReader` walks a directory cluster chain and groups
  `VfatLfnEntry` slots (attribute 0x0F) with their owning 8.3
  `MutableFatDirectoryEntry`.
- `LongFileNameCodec` decodes the UCS-2 little-endian fragments at offsets
  1..10 (5 chars), 14..25 (6 chars) and 28..31 (2 chars), reverses the
  ordinal order indicated by the sequence byte, strips the 0xFFFF padding and
  computes the 8.3 short-name checksum required by the spec.
- `VfatDirectoryRecord` exposes both the long name and the underlying short
  name, so callers can fall back to the 8.3 entry when no LFN slots are
  present.

---

## Partitioned hard disk images

Partitioned images (MBR + first partition) are supported for read and
write:

- `MbrCodec` parses the 512-byte master boot record, including the magic
  bytes 0x55 0xAA at offset 510..511 and four 16-byte partition table entries
  at offset 446.
- `PartitionTableEntry` exposes status, CHS start/end, type byte, absolute
  LBA start (`AbsSectStart`) and partition size in sectors (`PartSize`).
- `PartitionTableValidator` reports overlapping partitions, zero-size
  partitions, out-of-range CHS triples and unrecognised type bytes via
  `PartitionValidationIssue` / `PartitionValidationSeverity`.
- `FatDiskImage` and `FatFileSystemWithPartition` wrap a raw disk image so
  the FAT filesystem operates at the correct byte offset
  (`AbsSectStart * BytesPerSector`).

Extended partitions, GPT, dynamic disks and LVM are out of scope.

---

## Filesystem mutation and write-back

`MutableFatFileSystem` keeps the parsed BPB, FAT table and directory
contents in memory and tracks a single `IsDirty` flag. Mutations go through:

- `WriteFile(path, content, allocationStrategy)` and `DeleteFile(path)` for
  files.
- `MakeDirectory(path)` and `RemoveDirectory(path)` for directories.
- `WriteBootSector(mutator)` for in-place BPB edits (cluster table and data
  area are not touched).

`CommitChanges(imageBuffer)` serialises the in-memory state back into the
512-byte sectors of the image buffer. Both FAT copies are written when
`BPB.NumberOfFats == 2`.

`FileBackedFatImage` ties the in-memory filesystem to a file on disk:

- `FileBackedFatImage.Open(path, FatType)` reads the file once into memory
  and exposes the live `MutableFatFileSystem`.
- `Flush()` calls `CommitChanges` and writes the buffer back to disk.
- `Dispose()` flushes only when `IsDirty` is true, so the file is left
  untouched when nothing was modified.

This mirrors the on-pause / on-exit write-back behaviour used by
dosbox-staging for FAT-backed mounted images (`src/dos/drive_fat.cpp`).

---

## Floppy image construction

`VirtualFloppyImage(sourceDirectory, Serilog.ILogger)` builds a 1.44 MB
DOS-bootable FAT12 floppy image from a host directory. The resulting
`byte[]` has the canonical layout:

| Region            | LBA range | Size           | Contents                                              |
|-------------------|-----------|----------------|--------------------------------------------------------|
| Boot sector / BPB | 0         | 512 bytes      | x86 jump, OEM id `"SPICE86 "`, BPB, 0x55 0xAA at 510   |
| FAT #1            | 1..9      | 4608 bytes     | FAT12 entries (media descriptor 0xF0 at FAT[0])        |
| FAT #2            | 10..18    | 4608 bytes     | Mirror of FAT #1                                       |
| Root directory    | 19..32    | 14 sectors     | 224 fixed-size 32-byte entries                         |
| Data area         | 33..2879  | 2847 sectors   | Files and sub-directories (1 cluster = 1 sector)       |

Subdirectories are traversed recursively. Names are normalised to upper-case
8.3 via `ToDosBaseName` / `ToDosFileName`. Files that do not fit in the
remaining clusters, and directories that exhaust the 16 entries per
subdirectory cluster, are skipped with a Serilog warning. Other floppy
geometries (360 KB DSDD, 720 KB DSDD, 1.2 MB HD, 2.88 MB ED) are not
generated by this builder; they can still be mounted as FAT12 through
`MutableFatFileSystem` if the caller provides a valid image buffer.

---

## Working set

Implemented:

- FAT12, FAT16 and FAT32 BPB read/write and cluster codec
- FAT12 / FAT16 / FAT32 file and directory read/write through
  `MutableFatFileSystem`
- VFAT long file name read
- MBR + first-partition read and validation
- Pause / exit write-back through `FileBackedFatImage`
- 1.44 MB FAT12 floppy image synthesis from a host directory

## Example

```csharp
using Spice86.Shared.Emulator.Storage.FileSystem;

// Build a 1.44 MB bootable-shape FAT12 image from a directory.
VirtualFloppyImage builder = new VirtualFloppyImage(@"C:\dos-files", Serilog.Core.Logger.None);
byte[] floppy = builder.Build();
File.WriteAllBytes(@"C:\dos.img", floppy);

// Mount it for in-place mutation and persist on Dispose.
using FileBackedFatImage image = FileBackedFatImage.Open(@"C:\dos.img", FatType.Fat12);
image.FileSystem.WriteFile("AUTOEXEC.BAT", File.ReadAllBytes(@"C:\custom-autoexec.bat"), new FirstFitAllocationStrategy());
```

---

## License

Apache-2.0. See `LICENSE` at the root of the [Spice86 repository](https://github.com/OpenRakis/Spice86).
