namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
///     Current directory of one mounted DOS drive, part of <see cref="DosState" />.
/// </summary>
/// <param name="DriveLetter">Letter of the drive.</param>
/// <param name="Directory">Current directory without the drive letter and the leading backslash.</param>
public readonly record struct DosDriveDirectoryState(char DriveLetter, string Directory);
