namespace Spice86.Shared.Interfaces;

/// <summary>
/// Provides methods to mount drives (floppy and CD-ROM) from the UI layer.
/// </summary>
public interface IDriveMountService {
    /// <summary>Mounts a host folder as a floppy drive.</summary>
    /// <param name="driveLetter">The drive letter (A or B).</param>
    /// <param name="hostPath">The host folder path.</param>
    /// <returns><see langword="true"/> when the mount succeeded; <see langword="false"/> on failure.</returns>
    bool MountFolderAsFloppy(char driveLetter, string hostPath);

    /// <summary>Mounts an image file as a floppy drive.</summary>
    /// <param name="driveLetter">The drive letter (A or B).</param>
    /// <param name="imagePath">The path to the disk image file.</param>
    /// <returns><see langword="true"/> when the mount succeeded; <see langword="false"/> on failure.</returns>
    bool MountImageAsFloppy(char driveLetter, string imagePath);

    /// <summary>Mounts a host folder as a CD-ROM drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="hostPath">The host folder path.</param>
    /// <returns><see langword="true"/> when the mount succeeded; <see langword="false"/> on failure.</returns>
    bool MountFolderAsCdRom(char driveLetter, string hostPath);

    /// <summary>Mounts an image file as a CD-ROM drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="imagePath">The path to the ISO/CUE image file.</param>
    /// <returns><see langword="true"/> when the mount succeeded; <see langword="false"/> on failure.</returns>
    bool MountImageAsCdRom(char driveLetter, string imagePath);
}
