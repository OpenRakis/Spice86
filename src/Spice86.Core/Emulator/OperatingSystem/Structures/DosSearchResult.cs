namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents a DOS search result for FindFirst/FindNext operations.
/// </summary>
/// <param name="Name"> Gets or sets the name of the file found. </param>
/// <param name="Size"> Gets or sets the size of the file found. </param>
/// <param name="Date"> Gets or sets the date of the file found. </param>
/// <param name="Time"> Gets or sets the time of the file found. </param>
/// <param name="Attributes"> Gets or sets the attributes of the file found. </param>
public record struct DosSearchResult(string Name, uint Size, ushort Date, ushort Time, DosFileAttributes Attributes) {
    /// <summary>
    /// Gets whether the file is actually a file (not a directory or volume label).
    /// </summary>
    public readonly bool IsFile => (Attributes & (DosFileAttributes.Directory | DosFileAttributes.VolumeId)) == 0;
}