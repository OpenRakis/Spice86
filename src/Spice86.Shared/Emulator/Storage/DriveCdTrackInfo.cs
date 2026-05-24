namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Read-only snapshot of a single track on a CD-ROM drive.
/// </summary>
/// <param name="Number">The track number (1-based).</param>
/// <param name="StartLba">The starting Logical Block Address of the track.</param>
/// <param name="LengthSectors">The length of the track in sectors.</param>
/// <param name="IsAudio"><see langword="true"/> if the track contains audio, <see langword="false"/> for data.</param>
public sealed record DriveCdTrackInfo(int Number, uint StartLba, uint LengthSectors, bool IsAudio);
