namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>Describes one entry returned by a DOS drive content provider.</summary>
public sealed record DosContentEntry(
    string Name,
    bool IsDirectory,
    uint Size,
    DosFileAttributes Attributes,
    DateTime CreationTimeUtc,
    string? HostPath);
