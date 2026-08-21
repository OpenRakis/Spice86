namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Diagnostics.CodeAnalysis;

/// <summary>Represents a valid DOS drive letter with no mounted media.</summary>
public sealed class EmptyDosDrive : DosDriveBase {
    [SetsRequiredMembers]
    public EmptyDosDrive(char driveLetter) {
        DriveLetter = driveLetter;
        IsRemovable = driveLetter is 'A' or 'B';
    }
}