namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

/// <summary>
/// Represents a floppy disk drive (A: or B:), optionally backed by a FAT12 disk image.
/// </summary>
public class FloppyDiskDrive : DosDriveBase {
    /// <summary>
    /// Gets the FAT12 filesystem for the mounted floppy image, or <see langword="null"/> when no image is mounted.
    /// </summary>
    public Fat12FileSystem? Image { get; private set; }

    /// <summary>
    /// Gets <see langword="true"/> when a floppy image is currently mounted in this drive.
    /// </summary>
    public bool HasImage => Image != null;

    /// <summary>Initialises a new empty (no media) floppy drive.</summary>
    public FloppyDiskDrive() {
        IsRemovable = true;
    }

    /// <summary>
    /// Mounts a FAT12 floppy image into this drive.
    /// </summary>
    /// <param name="imageData">The raw bytes of the .img floppy disk image.</param>
    public void MountImage(byte[] imageData) {
        Image = new Fat12FileSystem(imageData);
        Label = Image.VolumeLabel;
    }
}
