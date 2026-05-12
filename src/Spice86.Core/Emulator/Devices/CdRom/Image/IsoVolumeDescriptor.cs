namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Holds the fields from an ISO 9660 Primary Volume Descriptor relevant to DOS emulation.</summary>
public sealed class IsoVolumeDescriptor {
    /// <summary>Gets the volume label (up to 32 characters, trimmed of trailing spaces).</summary>
    public string VolumeIdentifier { get; }

    /// <summary>Gets the logical block address of the root directory.</summary>
    public int RootDirectoryLba { get; }

    /// <summary>Gets the size of the root directory in bytes.</summary>
    public int RootDirectorySize { get; }

    /// <summary>Gets the logical block size in bytes (typically 2048).</summary>
    public int LogicalBlockSize { get; }

    /// <summary>Gets the total number of logical blocks on the disc.</summary>
    public int VolumeSpaceSize { get; }

    /// <summary>Initialises a new <see cref="IsoVolumeDescriptor"/>.</summary>
    public IsoVolumeDescriptor(
        string volumeIdentifier,
        int rootDirectoryLba,
        int rootDirectorySize,
        int logicalBlockSize,
        int volumeSpaceSize) {
        VolumeIdentifier = volumeIdentifier;
        RootDirectoryLba = rootDirectoryLba;
        RootDirectorySize = rootDirectorySize;
        LogicalBlockSize = logicalBlockSize;
        VolumeSpaceSize = volumeSpaceSize;
    }
}
