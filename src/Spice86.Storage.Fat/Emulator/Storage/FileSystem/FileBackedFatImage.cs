namespace Spice86.Shared.Emulator.Storage.FileSystem;

using System;
using System.IO;

/// <summary>
/// Couples a FAT image file on disk with an in-memory <see cref="MutableFatFileSystem"/>
/// and persists changes back to the file on demand.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the on-pause / on-exit write-back strategy used by dosbox-staging
/// for FAT-backed mounted images (see <c>src/dos/drive_fat.cpp</c>): the
/// image bytes are loaded once, mutated through the FAT API, and serialized
/// back via <see cref="MutableFatFileSystem.CommitChanges"/>.
/// </para>
/// <para>
/// Disposing the instance automatically flushes if the underlying filesystem
/// is still dirty, so callers cannot accidentally lose pending changes.
/// </para>
/// </remarks>
public sealed class FileBackedFatImage : IDisposable {
    private readonly byte[] _imageBytes;
    private bool _disposed;

    private FileBackedFatImage(string path, byte[] imageBytes, MutableFatFileSystem fileSystem) {
        Path = path;
        _imageBytes = imageBytes;
        FileSystem = fileSystem;
    }

    /// <summary>Gets the absolute path of the backing image file on disk.</summary>
    public string Path { get; }

    /// <summary>Gets the live in-memory FAT filesystem mutated by callers.</summary>
    public MutableFatFileSystem FileSystem { get; }

    /// <summary>
    /// Opens an existing FAT image file from disk and parses it as the supplied
    /// <paramref name="fatType"/>.
    /// </summary>
    /// <param name="path">Absolute path of the image file to open.</param>
    /// <param name="fatType">Expected FAT variant (FAT12 / FAT16 / FAT32).</param>
    /// <returns>A new <see cref="FileBackedFatImage"/> bound to the file.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist on disk.</exception>
    public static FileBackedFatImage Open(string path, FatType fatType) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException("FAT image file not found.", path);
        }

        byte[] imageBytes = File.ReadAllBytes(path);
        MutableFatFileSystem fileSystem = new MutableFatFileSystem(imageBytes, fatType);
        return new FileBackedFatImage(path, imageBytes, fileSystem);
    }

    /// <summary>
    /// Serializes any pending FAT mutations into the in-memory image buffer
    /// (via <see cref="MutableFatFileSystem.CommitChanges"/>) and writes the
    /// buffer back to the backing file on disk.
    /// </summary>
    public void Flush() {
        ObjectDisposedException.ThrowIf(_disposed, this);

        FileSystem.CommitChanges(_imageBytes);
        File.WriteAllBytes(Path, _imageBytes);
    }

    /// <summary>
    /// Flushes pending changes if the filesystem is dirty and marks this
    /// instance disposed.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (FileSystem.IsDirty) {
            Flush();
        }

        _disposed = true;
    }
}
