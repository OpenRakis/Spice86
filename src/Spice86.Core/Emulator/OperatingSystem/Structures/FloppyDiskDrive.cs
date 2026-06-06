namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Shared.Emulator.Storage.FileSystem;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

/// <summary>
/// Represents a floppy disk drive (A: or B:), backed by one or more raw disk images.
/// When the current image contains a FAT volume, <see cref="Image"/> exposes a derived filesystem view.
/// When more than one image is registered, Ctrl-F4 disc switching cycles through them.
/// </summary>
public class FloppyDiskDrive : VirtualDrive, System.IDisposable {
    private readonly List<(byte[] Data, string Path, bool IsDirty, int PartitionByteOffset)> _images = new();
    private int _currentIndex;

    /// <summary>
    /// Gets the FAT filesystem view for the mounted image, or <see langword="null"/> when
    /// the current image is not a FAT volume.
    /// Supports FAT12, FAT16, and FAT32 images.
    /// </summary>
    public FatFileSystem? Image { get; private set; }

    /// <summary>
    /// Gets <see langword="true"/> when a raw image is currently mounted in this drive.
    /// </summary>
    public bool HasImage => _images.Count > 0;

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
    [SetsRequiredMembers]
    public FloppyDiskDrive() {
        MountedHostDirectory = string.Empty;
        IsRemovable = true;
    }

    /// <summary>Gets a value indicating whether the current image has been modified since the last flush.</summary>
    public bool IsDirty => _images.Count > 0 && _images[_currentIndex].IsDirty;

    /// <summary>Gets a value indicating whether any registered image has pending dirty bytes.</summary>
    public bool HasDirtyImages => _images.Any(static image => image.IsDirty);

    /// <summary>Marks the current image as modified so that <see cref="FlushToDisk"/> will persist it.</summary>
    public void MarkDirty() {
        if (_images.Count == 0) {
            return;
        }
        (byte[] data, string path, bool _, int partitionByteOffset) = _images[_currentIndex];
        _images[_currentIndex] = (data, path, true, partitionByteOffset);
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
        (byte[] d, string p, bool _, int partitionByteOffset) = _images[_currentIndex];
        _images[_currentIndex] = (d, p, false, partitionByteOffset);
    }

    /// <summary>
    /// Mounts a FAT12 floppy image into this drive. If images are already registered,
    /// the new image is added to the list and becomes the current one.
    /// </summary>
    /// <param name="imageData">The raw bytes of the .img floppy disk image.</param>
    /// <param name="imagePath">The host file-system path of the image (used for display).</param>
    public void MountImage(byte[] imageData, string imagePath) {
        MountImage(imageData, imagePath, partitionByteOffset: 0);
    }

    /// <summary>
    /// Mounts a FAT image into this drive at the specified partition byte offset.
    /// Use a non-zero offset when the host file is an MBR-partitioned disk image and the
    /// active partition's FAT volume begins inside the file rather than at byte 0.
    /// The full image bytes remain available via <see cref="GetCurrentImageData"/> for INT 13h
    /// LBA sector access; only the FAT filesystem view (<see cref="Image"/>) is sliced.
    /// </summary>
    /// <param name="imageData">The raw bytes of the disk image (entire host file).</param>
    /// <param name="imagePath">The host file-system path of the image (used for display and flush).</param>
    /// <param name="partitionByteOffset">Byte offset of the active partition's first sector within <paramref name="imageData"/>.</param>
    public void MountImage(byte[] imageData, string imagePath, int partitionByteOffset) {
        _images.Add((imageData, imagePath, false, partitionByteOffset));
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
        _images.Add((imageData, imagePath, false, 0));
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
        (byte[] data, string _, bool _, int partitionByteOffset) = _images[_currentIndex];
        byte[] fatViewBytes = data;
        if (partitionByteOffset > 0) {
            int sliceLength = data.Length - partitionByteOffset;
            fatViewBytes = new byte[sliceLength];
            System.Buffer.BlockCopy(data, partitionByteOffset, fatViewBytes, 0, sliceLength);
        }
        try {
            Image = new FatFileSystem(fatViewBytes);
            Label = Image.VolumeLabel;
        } catch (InvalidDataException) {
            Image = null;
            Label = string.Empty;
        } catch (ArgumentOutOfRangeException) {
            Image = null;
            Label = string.Empty;
        }
    }

    /// <summary>
    /// Flushes any dirty images back to disk so guest-side writes survive an unmount or emulator exit.
    /// Mirrors dosbox-staging's drive_fat destructor behaviour.
    /// </summary>
    public void Dispose() {
        FlushDirtyImagesToDisk();
    }

    /// <summary>
    /// Flushes all dirty registered images back to their host image paths.
    /// </summary>
    /// <returns>The number of images that were actually written to disk.</returns>
    public int FlushDirtyImagesToDisk() {
        int flushedImageCount = 0;
        for (int i = 0; i < _images.Count; i++) {
            (byte[] data, string path, bool isDirty, int partitionByteOffset) = _images[i];
            if (!isDirty) {
                continue;
            }
            if (string.IsNullOrEmpty(path)) {
                continue;
            }
            File.WriteAllBytes(path, data);
            _images[i] = (data, path, false, partitionByteOffset);
            flushedImageCount++;
        }

        return flushedImageCount;
    }
}
