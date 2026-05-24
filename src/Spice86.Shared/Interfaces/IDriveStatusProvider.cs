namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;

/// <summary>
/// Provides a snapshot of all DOS drive statuses for polling by the UI.
/// </summary>
/// <remarks>
/// The UI should call <see cref="GetDriveStatuses"/> on a timer rather than
/// subscribing to events; the emulator is free to change drives at any time.
/// </remarks>
public interface IDriveStatusProvider {
    /// <summary>
    /// Returns the current status of every mounted DOS drive.
    /// </summary>
    /// <returns>
    /// A read-only list of <see cref="DosVirtualDriveStatus"/> snapshots, one per drive.
    /// The list is ordered by drive letter (A, B, C, …).
    /// </returns>
    IReadOnlyList<DosVirtualDriveStatus> GetDriveStatuses();
}
