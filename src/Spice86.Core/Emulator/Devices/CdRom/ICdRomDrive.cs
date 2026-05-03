using System.Collections.Generic;

using Spice86.Core.Emulator.Devices.CdRom.Image;

namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Represents the engine layer of a CD-ROM drive, sitting above the raw image layer.</summary>
public interface ICdRomDrive {
    /// <summary>Gets the currently mounted CD-ROM image.</summary>
    ICdRomImage Image { get; }

    /// <summary>Gets the physical media and door state of the drive.</summary>
    CdRomMediaState MediaState { get; }

    /// <summary>Gets a value indicating whether audio is currently playing.</summary>
    bool IsAudioPlaying { get; }

    /// <summary>Reads sector data from the disc starting at the given logical block address.</summary>
    /// <param name="lba">Logical block address of the first sector to read.</param>
    /// <param name="sectorCount">Number of sectors to read.</param>
    /// <param name="destination">Buffer that receives the sector data.</param>
    /// <param name="mode">Requested sector encoding mode.</param>
    /// <returns>Total number of bytes written into <paramref name="destination"/>.</returns>
    int Read(int lba, int sectorCount, Span<byte> destination, CdSectorMode mode);

    /// <summary>Returns the full table of contents for the currently mounted disc.</summary>
    /// <returns>An ordered list of TOC entries including the lead-out.</returns>
    IReadOnlyList<TableOfContentsEntry> GetTableOfContents();

    /// <summary>Returns the TOC entry for the specified track number.</summary>
    /// <param name="trackNumber">The track number to look up.</param>
    /// <returns>The matching <see cref="TableOfContentsEntry"/>, or <see langword="null"/> if not found.</returns>
    TableOfContentsEntry? GetTrackInfo(int trackNumber);

    /// <summary>Returns summary information about the currently mounted disc.</summary>
    DiscInfo GetDiscInfo();

    /// <summary>Begins audio playback from the specified logical block address.</summary>
    /// <param name="startLba">The LBA at which playback starts.</param>
    /// <param name="sectorCount">The number of sectors to play.</param>
    void PlayAudio(int startLba, int sectorCount);

    /// <summary>Stops audio playback.</summary>
    void StopAudio();

    /// <summary>Resumes audio playback if it was paused.</summary>
    void ResumeAudio();

    /// <summary>Returns the current audio playback state.</summary>
    /// <returns>A <see cref="CdAudioPlayback"/> describing the current or last playback.</returns>
    CdAudioPlayback GetAudioStatus();

    /// <summary>Returns the UPC/EAN catalogue number of the disc, or <see langword="null"/> if not present.</summary>
    string? GetUpc();

    /// <summary>Opens the drive door and notifies that media has changed.</summary>
    void Eject();

    /// <summary>Inserts a new image into the drive, closes the door, and notifies that media has changed.</summary>
    /// <param name="image">The image to mount.</param>
    void Insert(ICdRomImage image);
}
