namespace Spice86.Shared.Interfaces;

using System;

/// <summary>Event payload carrying the drive letter that emitted activity.</summary>
public sealed class DriveActivityEventArgs : EventArgs {
    /// <summary>Gets the upper-case drive letter that emitted the activity.</summary>
    public char DriveLetter { get; }

    /// <summary>Initialises a new <see cref="DriveActivityEventArgs"/>.</summary>
    /// <param name="driveLetter">The drive letter that emitted the activity (case-insensitive).</param>
    public DriveActivityEventArgs(char driveLetter) {
        DriveLetter = char.ToUpperInvariant(driveLetter);
    }
}
