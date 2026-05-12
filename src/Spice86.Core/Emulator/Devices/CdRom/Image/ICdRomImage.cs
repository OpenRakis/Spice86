using System.Collections.Generic;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Represents a mountable CD-ROM image (ISO or CUE/BIN).</summary>
public interface ICdRomImage : IDisposable {
    /// <summary>Gets the ordered list of tracks on the disc.</summary>
    IReadOnlyList<CdTrack> Tracks { get; }

    /// <summary>Gets the total number of sectors on the disc.</summary>
    int TotalSectors { get; }

    /// <summary>Reads sector data from the disc at the given logical block address.</summary>
    /// <param name="lba">Logical block address of the first sector to read.</param>
    /// <param name="destination">Buffer that receives the sector data.</param>
    /// <param name="mode">Requested sector encoding mode.</param>
    /// <returns>Number of bytes written into <paramref name="destination"/>.</returns>
    int Read(int lba, Span<byte> destination, CdSectorMode mode);

    /// <summary>Gets the parsed ISO 9660 Primary Volume Descriptor.</summary>
    IsoVolumeDescriptor PrimaryVolume { get; }

    /// <summary>Gets the UPC/EAN catalogue number of the disc, or <see langword="null"/> if not present.</summary>
    string? UpcEan { get; }

    /// <summary>Gets the path to the image file on disk.</summary>
    string ImagePath { get; }
}
