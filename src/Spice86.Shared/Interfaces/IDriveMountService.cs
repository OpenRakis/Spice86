namespace Spice86.Shared.Interfaces;

/// <summary>
/// Provides methods to mount drives (floppy and CD-ROM) from the UI layer.
/// </summary>
public interface IDriveMountService {
    /// <summary>Mounts a host folder as a floppy drive.</summary>
    /// <param name="driveLetter">The drive letter (A or B).</param>
    /// <param name="hostPath">The host folder path.</param>
    void MountFolderAsFloppy(char driveLetter, string hostPath);

    /// <summary>Mounts an image file as a floppy drive.</summary>
    /// <param name="driveLetter">The drive letter (A or B).</param>
    /// <param name="imagePath">The path to the disk image file.</param>
    void MountImageAsFloppy(char driveLetter, string imagePath);

    /// <summary>Mounts a host folder as a CD-ROM drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="hostPath">The host folder path.</param>
    void MountFolderAsCdRom(char driveLetter, string hostPath);

    /// <summary>Mounts an image file as a CD-ROM drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="imagePath">The path to the ISO/CUE image file.</param>
    void MountImageAsCdRom(char driveLetter, string imagePath);
}
