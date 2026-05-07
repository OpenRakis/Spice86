namespace Spice86.Shared.Interfaces;

using System;

/// <summary>Default <see cref="IDriveActivityNotifier"/>: simple thread-safe event broadcaster.</summary>
public sealed class DriveActivityNotifier : IDriveActivityNotifier {
    /// <inheritdoc />
    public event EventHandler<DriveActivityEventArgs>? Read;

    /// <inheritdoc />
    public event EventHandler<DriveActivityEventArgs>? Write;

    /// <inheritdoc />
    public void NotifyRead(char driveLetter) {
        Read?.Invoke(this, new DriveActivityEventArgs(driveLetter));
    }

    /// <inheritdoc />
    public void NotifyWrite(char driveLetter) {
        Write?.Invoke(this, new DriveActivityEventArgs(driveLetter));
    }
}
