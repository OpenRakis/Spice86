namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Read-only snapshot of one cluster's state on a FAT-formatted drive.
/// </summary>
/// <param name="Index">Zero-based cluster index in the bitmap.</param>
/// <param name="State">The cluster's allocation state.</param>
public sealed record DriveClusterInfo(int Index, DriveClusterState State);
