namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

/// <summary>Identifies the type of File Allocation Table used in a volume.</summary>
public enum FatType {
    /// <summary>FAT12, used on floppy disks up to 32 MB.</summary>
    Fat12,
    /// <summary>FAT16, used on volumes up to 2 GB.</summary>
    Fat16,
    /// <summary>FAT32, used on volumes up to 2 TB.</summary>
    Fat32
}
