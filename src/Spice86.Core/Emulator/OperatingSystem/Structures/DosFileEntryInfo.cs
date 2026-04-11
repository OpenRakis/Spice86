namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Encapsulates the metadata of a DOS file system entry (file or directory)
/// as resolved by <see cref="DosPathResolver"/>.
/// </summary>
/// <param name="Attributes">The DOS file attributes.</param>
/// <param name="FileSize">The size in bytes (0 for directories).</param>
/// <param name="CreationTimeUtc">The UTC creation timestamp.</param>
/// <param name="ShortName">The 8.3 short file name.</param>
internal record struct DosFileEntryInfo(
    DosFileAttributes Attributes,
    uint FileSize,
    DateTime CreationTimeUtc,
    string ShortName);
