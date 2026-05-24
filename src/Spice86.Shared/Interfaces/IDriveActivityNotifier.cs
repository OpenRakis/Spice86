namespace Spice86.Shared.Interfaces;

using System;

/// <summary>Pushes per-drive read/write activity events to UI subscribers.</summary>
public interface IDriveActivityNotifier {
    /// <summary>Raised when a read operation hits a DOS drive.</summary>
    event EventHandler<DriveActivityEventArgs>? Read;
    /// <summary>Raised when a write operation hits a DOS drive.</summary>
    event EventHandler<DriveActivityEventArgs>? Write;
    /// <summary>Records a read on the given drive letter.</summary>
    void NotifyRead(char driveLetter);
    /// <summary>Records a write on the given drive letter.</summary>
    void NotifyWrite(char driveLetter);
}
