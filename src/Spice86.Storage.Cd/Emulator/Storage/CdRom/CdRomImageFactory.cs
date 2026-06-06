namespace Spice86.Shared.Emulator.Storage.CdRom;

using System.Collections.Generic;

/// <summary>Creates an <see cref="ICdRomImage"/> from an image file path.</summary>
public static class CdRomImageFactory {
    private const int CookedSectorSize = 2048;
    private const int RawSectorSize = 2352;
    private const int Mode2Form2SectorSize = 2336;
    private const int IsoPvdLba = 16;
    private static readonly byte[] IsoSignature = "CD001"u8.ToArray();

    /// <summary>
    /// Opens the disc image at <paramref name="imagePath"/>, choosing the implementation based on file
    /// extension and content sniffing. Recognised extensions are <c>.cue</c>, <c>.mds</c>, <c>.iso</c>,
    /// and raw dumps (<c>.bin</c>, <c>.img</c>, <c>.gog</c>). Raw dumps are accepted with or without a
    /// sibling <c>.cue</c> file: when no sibling is present the file is
    /// sniffed for an ISO 9660 signature, then its length is matched against a known sector size, and
    /// finally MODE1/2352 is assumed as a permissive last resort. Any unknown extension is treated the
    /// same way so the call never fails for a file that exists.
    /// </summary>
    /// <param name="imagePath">Path to the disc image.</param>
    /// <returns>An <see cref="ICdRomImage"/> ready for reading.</returns>
    public static ICdRomImage Open(string imagePath) {
        string extension = Path.GetExtension(imagePath);
        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)) {
            return new CueBinImage(imagePath);
        }
        if (extension.Equals(".mds", StringComparison.OrdinalIgnoreCase)) {
            return new MdsImage(imagePath);
        }
        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)) {
            return new IsoImage(imagePath);
        }
        // Raw dump extensions and any unknown extension are funnelled through the same sniff-and-synthesize path.
        return OpenRawDump(imagePath);
    }

    private static bool IsRawDumpExtension(string extension) {
        return extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".img", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gog", StringComparison.OrdinalIgnoreCase);
    }

    private static ICdRomImage OpenRawDump(string imagePath) {
        // Prefer a sibling .cue when present (standard BIN/CUE pair).
        string siblingCue = Path.ChangeExtension(imagePath, ".cue");
        if (File.Exists(siblingCue)) {
            return new CueBinImage(siblingCue);
        }

        // Cooked ISO renamed to .bin/.img: detect ISO 9660 signature at LBA 16.
        if (HasIsoSignatureAtLba16(imagePath, CookedSectorSize)) {
            return new IsoImage(imagePath);
        }

        // Raw 2352-byte/sector single-track MODE1/2352 dump: detect the same signature at the cooked offset within the raw sector.
        long length = new FileInfo(imagePath).Length;
        if (length > 0 && length % RawSectorSize == 0 && HasIsoSignatureInRawSector(imagePath, RawSectorSize)) {
            return new CueBinImage(BuildSyntheticCueSheet(imagePath, "MODE1/2352"), imagePath);
        }

        // Size-based fallback (matches DOSBox 'imgmount -t iso' tolerance).
        if (length > 0 && length % CookedSectorSize == 0) {
            return new CueBinImage(BuildSyntheticCueSheet(imagePath, "MODE1/2048"), imagePath);
        }
        if (length > 0 && length % Mode2Form2SectorSize == 0) {
            return new CueBinImage(BuildSyntheticCueSheet(imagePath, "MODE2/2336"), imagePath);
        }

        // Permissive last resort: treat as MODE1/2352. The track length will round down to whole sectors.
        return new CueBinImage(BuildSyntheticCueSheet(imagePath, "MODE1/2352"), imagePath);
    }

    private static bool HasIsoSignatureAtLba16(string path, int sectorSize) {
        long offset = (long)IsoPvdLba * sectorSize;
        byte[] buffer = new byte[6];
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < offset + buffer.Length) {
            return false;
        }
        stream.Seek(offset + 1, SeekOrigin.Begin);
        int read = stream.Read(buffer, 0, 5);
        if (read < 5) {
            return false;
        }
        return buffer.AsSpan(0, 5).SequenceEqual(IsoSignature);
    }

    private static bool HasIsoSignatureInRawSector(string path, int rawSectorSize) {
        // In a raw MODE1/2352 sector the cooked payload starts at byte 16; the PVD signature is at offset 1 within the cooked data.
        long offset = (long)IsoPvdLba * rawSectorSize + 16 + 1;
        byte[] buffer = new byte[5];
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < offset + buffer.Length) {
            return false;
        }
        stream.Seek(offset, SeekOrigin.Begin);
        int read = stream.Read(buffer, 0, 5);
        if (read < 5) {
            return false;
        }
        return buffer.AsSpan().SequenceEqual(IsoSignature);
    }

    private static CueSheet BuildSyntheticCueSheet(string binPath, string trackMode) {
        List<CueEntry> entries = new List<CueEntry> {
            new CueEntry {
                FileName = binPath,
                FileType = CueFileType.Binary,
                TrackMode = trackMode,
                TrackNumber = 1,
                IndexNumber = 1,
                // INDEX 01 at 00:02:00 (150 frames) per Red Book pre-gap convention; CueFrameMapper subtracts the 150-frame padding.
                IndexMsf = 150,
                Pregap = 0,
                Postgap = 0
            }
        };
        return new CueSheet(entries, string.Empty);
    }
}
