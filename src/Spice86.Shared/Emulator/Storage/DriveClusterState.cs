namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// State of a cluster in a FAT-formatted volume, used for content visualisation.
/// </summary>
public enum DriveClusterState {
    /// <summary>The cluster is free.</summary>
    Free,

    /// <summary>The cluster is allocated to a file or directory.</summary>
    Used,

    /// <summary>The cluster is marked as bad in the FAT.</summary>
    Bad,

    /// <summary>The cluster is reserved by the file system.</summary>
    Reserved
}
