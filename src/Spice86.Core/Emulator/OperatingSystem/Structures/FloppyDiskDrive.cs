namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Represents a floppy disk drive (A: or B:), optionally backed by one or more FAT12 disk images.
/// When more than one image is registered, Ctrl-F4 disc switching cycles through them.
/// </summary>
public class FloppyDiskDrive : DosDriveBase {
    private readonly List<(byte[] Data, string Path, bool IsDirty)> _images = new();
    private int _currentIndex;

    /// <summary>
    /// Gets the FAT filesystem for the mounted floppy image, or <see langword="null"/> when no image is mounted.
    /// Supports FAT12, FAT16, and FAT32 images.
    /// </summary>
    public FatFileSystem? Image { get; private set; }

    /// <summary>
    /// Gets <see langword="true"/> when a floppy image is currently mounted in this drive.
    /// </summary>
    public bool HasImage => Image != null;

    /// <summary>Gets the file-system path of the currently active disc image,
    /// or an empty string when the drive has no image mounted.
    /// </summary>
    public string ImagePath => _images.Count > 0 ? _images[_currentIndex].Path : string.Empty;

    /// <summary>
    /// Gets the total number of disc images registered for this drive.
    /// A value greater than 1 means Ctrl-F4 disc switching is available.
    /// </summary>
    public int ImageCount => _images.Count;

    /// <summary>Gets the file-system paths of all registered disc images in order.</summary>
    public IReadOnlyList<string> AllImagePaths => _images.Select(i => i.Path).ToList();

    /// <summary>Initialises a new empty (no media) floppy drive.</summary>
    public FloppyDiskDrive() {
        IsRemovable = true;
    }

    /// <summary>Gets a value indicating whether the current image has been modified since the last flush.</summary>
    public bool IsDirty => _images.Count > 0 && _images[_currentIndex].IsDirty;

    /// <summary>Marks the current image as modified so that <see cref="FlushToDisk"/> will persist it.</summary>
    public void MarkDirty() {
        if (_images.Count == 0) {
            return;
        }
        (byte[] data, string path, bool _) = _images[_currentIndex];
        _images[_currentIndex] = (data, path, true);
    }

    /// <summary>
    /// Writes the current image data back to <see cref="ImagePath"/> when the image is dirty.
    /// Does nothing when the image is clean or when no image path is set.
    /// </summary>
    public void FlushToDisk() {
        if (!IsDirty) {
            return;
        }
        if (string.IsNullOrEmpty(ImagePath)) {
            return;
        }
        byte[]? data = GetCurrentImageData();
        if (data == null) {
            return;
        }
        File.WriteAllBytes(ImagePath, data);
        (byte[] d, string p, bool _) = _images[_currentIndex];
        _images[_currentIndex] = (d, p, false);
    }

    /// <summary>
    /// Mounts a FAT12 floppy image into this drive. If images are already registered,
    /// the new image is added to the list and becomes the current one.
    /// </summary>
    /// <param name="imageData">The raw bytes of the .img floppy disk image.</param>
    /// <param name="imagePath">The host file-system path of the image (used for display).</param>
    public void MountImage(byte[] imageData, string imagePath) {
        _images.Add((imageData, imagePath, false));
        _currentIndex = _images.Count - 1;
        ApplyCurrentImage();
    }

    /// <summary>
    /// Adds an additional floppy image to this drive's list without switching to it.
    /// Use <see cref="SwapToNextImage"/> to cycle through images.
    /// </summary>
    /// <param name="imageData">The raw bytes of the .img floppy disk image.</param>
    /// <param name="imagePath">The host file-system path of the image (used for display).</param>
    public void AddImage(byte[] imageData, string imagePath) {
        _images.Add((imageData, imagePath, false));
    }

    /// <summary>
    /// Advances to the next image in the registered list, cycling back to the first after the last.
    /// Has no effect when only one image is registered.
    /// </summary>
    public void SwapToNextImage() {
        if (_images.Count <= 1) {
            return;
        }
        _currentIndex = (_currentIndex + 1) % _images.Count;
        ApplyCurrentImage();
    }

    /// <summary>Switches to the image at the specified zero-based index. Has no effect if the index is out of range.</summary>
    /// <param name="index">Zero-based index of the target image.</param>
    public void SwapToIndex(int index) {
        if (index < 0 || index >= _images.Count) {
            return;
        }
        _currentIndex = index;
        ApplyCurrentImage();
    }

    /// <summary>
    /// Returns the raw byte array for the currently active floppy image,
    /// or <see langword="null"/> when no image is mounted.
    /// The returned array is the live storage — writes to it are reflected in the mounted image.
    /// </summary>
    public byte[]? GetCurrentImageData() {
        if (_images.Count == 0) {
            return null;
        }
        return _images[_currentIndex].Data;
    }

    private void ApplyCurrentImage() {
        (byte[] data, string _, bool _) = _images[_currentIndex];
        Image = new FatFileSystem(data);
        Label = Image.VolumeLabel;
    }
}
