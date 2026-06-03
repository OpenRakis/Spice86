# Spice86.Storage.Cd

Read CD-ROM disc images (ISO 9660, CUE/BIN, Alcohol 120% MDS/MDF) and decode
CDDA audio (WAVE) from .NET, without any native dependency.

This assembly ships as a standalone NuGet package. It has no project
references (no Spice86 dependency) and uses only the .NET base class library.
The behavioural reference is dosbox-staging's CD-ROM stack in
`dosbox-staging/src/dos/cdrom_image.cpp` and `cdrom_mds.h`.

---

## CD-ROM standards (the "rainbow books")

CD-ROM image formats are layered on top of the physical CD specifications
known as the "rainbow books". This assembly is concerned with the data
geometry described by Red Book, Yellow Book and parts of Green Book / White
Book; ISO 9660 / Joliet sit on top.

| Book    | Spec                                  | Defines                                                                 | Coverage in this assembly |
|---------|----------------------------------------|-------------------------------------------------------------------------|---------------------------|
| Red     | IEC 60908 (CD-DA)                      | 44.1 kHz / 16-bit / stereo / 2352-byte audio frames                     | yes — `CdSectorMode.AudioRaw2352`, raw 2352 byte/sector audio tracks, WAV decoding through `WavAudioCodec` |
| Yellow  | ECMA-130 (CD-ROM Mode 1 / Mode 2)      | 2048-byte user data ("cooked") and 2352-byte raw frames                 | yes — `CdSectorMode.CookedData2048` (Mode 1, 2048) and `CdSectorMode.Raw2352` (Mode 1, 2352) |
| Green   | CD-i / CD-ROM XA                        | Mode 2 Form 1 (2048 data) and Mode 2 Form 2 (2324/2336 data) sub-headers | yes — `CdSectorMode.Mode2Form1` and `CdSectorMode.Mode2Form2` |
| Orange  | CD-R / CD-RW recordable specification   | Multi-session, recordable media, lead-in/lead-out                       | partial — single-session images only; MDS multi-session files are truncated to session 1 |
| White   | Video CD (MPEG-1 over Mode 2 Form 2)   | VCD application layout on top of XA                                     | data plumbing only — sectors readable through `Mode2Form2`; no MPEG decoding |
| Blue    | Enhanced Music CD (CD-EXTRA / CD-Plus) | Multi-session audio + data layout                                       | not supported — multi-session reads are limited to the first session |

ISO 9660 (ECMA-119) and the Joliet supplementary volume descriptor
(Microsoft, 1995) describe the logical filesystem inside a Yellow Book data
track. Both are parsed by `IsoImage` and used by `CueBinImage` and
`MdsImage` to expose `PrimaryVolume` / `JolietVolume` metadata.

---

## Supported image formats

Namespace root: `Spice86.Shared.Emulator.Storage.CdRom`.

| Format         | Type                                  | Reader                          | Notes                                                                 |
|----------------|---------------------------------------|---------------------------------|-----------------------------------------------------------------------|
| `.iso`         | Plain ISO 9660 image                  | `IsoImage`                      | Single Mode 1 / 2048 data track. Parses PVD + optional Joliet SVD.    |
| `.cue` + `.bin`| CUE sheet with one or more BIN files  | `CueBinImage`                   | Multi-track. Supports `MODE1/2048`, `MODE1/2352`, `MODE2/2336`, `MODE2/2352`, `AUDIO`, plus `WAVE` FILE references. |
| `.mds` + `.mdf`| Alcohol 120% disc descriptor          | `MdsImage`                      | Parses MEDIA DESCRIPTOR header + session + track blocks. Multi-session images are truncated to session 1 (dosbox-staging parity). |

All readers expose the same `ICdRomImage` interface:

```csharp
public interface ICdRomImage : IDisposable {
    IReadOnlyList<CdTrack> Tracks { get; }
    int TotalSectors { get; }
    int Read(int lba, Span<byte> destination, CdSectorMode mode);
    IsoVolumeDescriptor PrimaryVolume { get; }
    string? UpcEan { get; }
    string ImagePath { get; }
}
```

A factory selects the implementation from the file extension:

```csharp
using Spice86.Shared.Emulator.Storage.CdRom;

using ICdRomImage disc = CdRomImageFactory.Open(@"C:\game.cue");
Span<byte> buffer = stackalloc byte[2048];
disc.Read(lba: 16, buffer, CdSectorMode.CookedData2048); // ISO 9660 PVD
```

---

## Sector modes

`CdSectorMode` captures the on-disc encodings recognised by this assembly.
They correspond one-to-one to the modes accepted by dosbox-staging's CUE
parser (`MODE1/2048`, `MODE1/2352`, `MODE2/2336`, `MODE2/2352`, `AUDIO`):

| Mode                  | Sector size | Usable data | Maps to dosbox-staging |
|-----------------------|-------------|-------------|------------------------|
| `CookedData2048`      | 2048 bytes  | 2048 bytes  | `MODE1/2048`           |
| `Raw2352`             | 2352 bytes  | 2048 bytes (Mode 1 user data after 16-byte sync/header) | `MODE1/2352` |
| `Mode2Form1`          | 2352 bytes  | 2048 bytes (XA Form 1, after 24-byte sub-header) | `MODE2/2352` (form 1) |
| `Mode2Form2`          | 2336 bytes  | 2324 bytes (XA Form 2, raw user data) | `MODE2/2336` |
| `AudioRaw2352`        | 2352 bytes  | Red Book PCM | `AUDIO`                |

`SectorFraming` and `CueFrameMapper` handle the byte-offset arithmetic for
Mode 1 / Mode 2 sync, header and sub-header sizes, so the `Read(lba, ...)`
API always returns user data in the requested mode.

---

## CUE/BIN parser

`CueSheetParser.Parse(cuePath)` is a tolerant CUE sheet parser that
implements the directives required by dosbox-staging and real-world DOS
game images:

- `CATALOG <upc-ean>` (13 ASCII digits) — exposed as
  `ICdRomImage.UpcEan`.
- `FILE "<name>" <type>` — file reference. Supported types are listed in
  `CueFileType`: `BINARY`, `MOTOROLA`, `WAVE`, `AIFF`, `MP3`, `FLAC`,
  `OGG`, `OPUS`. Audio decoding is dispatched through `IAudioCodecFactory`.
- `TRACK <nn> <mode>` — track number 1..99 with one of the modes mapped
  above.
- `PREGAP MM:SS:FF` and `POSTGAP MM:SS:FF` — silent frames before/after a
  track. Parsed and surfaced as `CueTrackLayout.PregapFrames` /
  `PostgapFrames`.
- `INDEX <nn> MM:SS:FF` — multiple index points per track. Index 00 is the
  pregap start, index 01 is the track start.
- `ISRC <code>` — accepted and ignored (no MSCDEX consumer in scope).
- `REM ...` — comments are ignored.

`CueFrameMapper.BuildLayout` resolves absolute LBA positions across all
tracks and BIN files, including implicit pregap derived from the difference
between `INDEX 00` and `INDEX 01`.

Audio support is intentionally limited to WAV (`WavAudioCodec` /
`WavAudioCodecFactory`). `DefaultAudioCodecFactory.Create()` returns a
`CompositeAudioCodecFactory` wired with WAV only. Callers can extend the
factory by composing additional `IAudioCodecFactory` implementations; the
CUE parser will then accept the corresponding `FILE ... <TYPE>` references.

---

## MDS / MDF parser

`MdsImage` opens the `.mds` descriptor file, defers parsing to `MdsParser`,
and binds the resolved tracks to one or more `.mdf` data files via
`FileBackedDataSource`.

The on-disk MDS layout matches the format documented in dosbox-staging's
`cdrom_mds.h` (originally de-glib'd from cdemu / libmirage):

- 88-byte header. Validated by `MdsParser.Signature`
  (`"MEDIA DESCRIPTOR"`).
- Session block (24 bytes) at `session_block_offset`. `num_all_blocks`
  tracks are read consecutively from `track_block_offset`.
- Each 80-byte track block references an 8-byte extra block (track length
  in sectors) and a 16-byte footer (filename pointer, ANSI/UTF-16 flag).
- `MdsTrackMode` recognises Red Book audio (attribute 0x00), Mode 1 data
  (attribute 0x40, mode2 = false) and Mode 2 data (attribute 0x40,
  mode2 = true). DVD-only XA forms 4/5/6 are rejected, matching
  dosbox-staging's `set_track_mode`.
- Non-track entries (point &lt; 1 or &gt; 99) are skipped.
- A synthetic lead-out entry is appended after the last track so callers
  can iterate up to the end of the disc.
- Multi-session MDS files are truncated to the first session
  (dosbox-staging parity).

`*.mdf` filenames inside the descriptor resolve relative to the directory
that contains the `.mds`. A footer filename of `"*.mdf"` is rewritten to
`<descriptor>.mdf` (Alcohol 120% convention).

---

## ISO 9660 and Joliet

`IsoImage` (and the embedded ISO parsing inside `CueBinImage` /
`MdsImage`) reads the Primary Volume Descriptor at LBA 16, validates the
`"CD001"` signature at offset 1, and exposes:

- Volume identifier (32 ASCII chars, trimmed).
- Logical block size (typically 2048).
- Volume space size in sectors.
- Root directory LBA and size.

`IsoSupplementaryVolumeDescriptor` decodes the optional Joliet SVD,
recognised by the escape sequence at offset 88 being one of
`%2F40`, `%2F43`, `%2F45` (UCS-2 BE levels 1/2/3). When present,
`IsoImage.JolietVolume` is populated and
`IsoImage.ReadJolietRootDirectory()` returns the long-name entries decoded
with `Encoding.BigEndianUnicode`.

Rock Ridge (POSIX file metadata) and El Torito (bootable CDs) are not
parsed in this version.

---

## Data sources and lifecycle

All image readers consume sector bytes through `IDataSource`. The
implementations included in this assembly:

- `FileBackedDataSource` — owns a `FileStream`, reads sectors from disk
  with an internal lock for thread safety, exposes `LengthBytes`.
- `MemoryDataSource` — wraps a `byte[]` (used by unit tests and the
  virtual ISO builder).
- `WindowedDataSource` — restricts a parent source to a `[offset, length)`
  window (used to slice multi-track BIN files into per-track views).
- `WavAudioFile` — exposes a WAV PCM payload as a `IDataSource` whose bytes
  are CDDA-ordered samples.

`ICdRomImage` is `IDisposable`; disposing the image disposes every owned
`FileBackedDataSource` and codec instance.

---

## Image construction

`VirtualIsoImage` builds a minimal ISO 9660 single-track image in memory
from a host directory. It writes a Primary Volume Descriptor, a Volume
Descriptor Set Terminator and a single-level root directory; it is
intended for tests and tooling rather than for authoring distribution
discs.

---

## Working set

Implemented:

- ISO 9660 Primary Volume Descriptor (read).
- Joliet Supplementary Volume Descriptor (read, UCS-2 BE).
- CUE/BIN parsing with all five dosbox-staging-recognised track modes.
- WAV audio file decoding for CDDA tracks in CUE sheets.
- Alcohol 120% MDS/MDF parsing with synthetic lead-out (first session only).
- Sector-level `Read(lba, span, mode)` API on all image types.
- UPC/EAN catalogue passthrough.
- In-memory ISO image construction via `VirtualIsoImage`.

Gaps versus dosbox-staging: none for the DOS workload in scope. Compressed
CDDA decoding (FLAC / OGG Vorbis / Opus / MP3) is intentionally left as an
`IAudioCodecFactory` extension point; dosbox-staging routes those file
types through SDL_sound (`cdrom_image.cpp:206` `Sound_NewSampleFromFile`,
`:412` `Sound_Decode_Direct`), which is a separate native dependency that
this assembly does not adopt.

---

## Completeness vs. dosbox-staging

Reference: `dosbox-staging/src/dos/cdrom_image.cpp` and `cdrom_mds.h`. The
load entry point in dosbox-staging is
`LoadMdsFile || LoadCueSheet || LoadIsoFile`; `CdRomImageFactory.Open`
dispatches on extension for the same three formats.

| Feature                                  | dosbox-staging | Spice86.Storage.Cd |
|------------------------------------------|----------------|--------------------|
| `.iso` (Mode 1 / 2048 single track)      | yes            | yes                |
| `.cue` / `.bin` (multi-track)            | yes            | yes                |
| `MODE1/2048` track                       | yes            | yes                |
| `MODE1/2352` track                       | yes            | yes                |
| `MODE2/2336` track                       | yes            | yes                |
| `MODE2/2352` track (XA Form 1)           | yes            | yes                |
| `AUDIO` track (Red Book)                 | yes            | yes                |
| `PREGAP` / `POSTGAP`                     | yes            | yes                |
| `INDEX 00`-derived implicit pregap       | yes            | yes                |
| `CATALOG` UPC/EAN                        | yes            | yes                |
| `ISRC`                                   | yes (ignored)  | yes (ignored)      |
| `.mds` / `.mdf` (Alcohol 120%)           | yes            | yes                |
| Multi-session                            | first session only | first session only |
| Joliet SVD                               | yes            | yes                |
| FILE WAVE                                | yes            | yes                |
| Compressed CDDA codecs (FLAC/OGG/OPUS/MP3) | yes (via SDL_sound, `cdrom_image.cpp:206`) | extension point via `IAudioCodecFactory` |
| Sector-level `Read(lba, mode)`           | yes            | yes                |

Soundness: the CUE/MDS parsers are line-/byte-faithful to the dosbox-staging
reference and are covered by integration tests over real disc images
(see `Spice86.Tests/Emulator/Storage/...`). Sector framing is verified by
round-tripping Mode 1 / Mode 2 / Audio sectors.

Completeness: the "everyday" CD-ROM surface used by DOS games — single
session, ISO 9660 + Joliet, CUE/BIN with audio tracks, MDS/MDF, WAV CDDA —
is at parity with dosbox-staging. No remaining gaps versus dosbox-staging
for the in-scope DOS workload; compressed CDDA decoding is intentionally an
`IAudioCodecFactory` extension point for downstream consumers.

---

## Example

```csharp
using Spice86.Shared.Emulator.Storage.CdRom;

using ICdRomImage disc = CdRomImageFactory.Open(@"C:\game.cue");
Console.WriteLine($"Volume:  {disc.PrimaryVolume.VolumeIdentifier}");
Console.WriteLine($"Tracks:  {disc.Tracks.Count}");
Console.WriteLine($"Sectors: {disc.TotalSectors}");

foreach (CdTrack track in disc.Tracks) {
    Console.WriteLine($"  #{track.Number,-2} {track.Mode,-15} start={track.StartLba,-8} length={track.LengthSectors}");
}

byte[] pvdSector = new byte[2048];
disc.Read(16, pvdSector, CdSectorMode.CookedData2048);
```

---

## License

Apache-2.0. See `LICENSE` at the root of the [Spice86 repository](https://github.com/OpenRakis/Spice86).
