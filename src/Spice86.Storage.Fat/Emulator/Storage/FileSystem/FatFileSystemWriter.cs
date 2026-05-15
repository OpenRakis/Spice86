namespace Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// Orchestrates serialization of a mutable FAT filesystem to disk image.
/// Ensures all FAT copies are identical and boot sector is valid.
/// </summary>
public sealed class FatFileSystemWriter
{
    /// <summary>
    /// Serializes a mutable FAT filesystem to a disk image.
    /// Writes boot sector, all FAT copies (ensuring they are identical),
    /// and directory sectors with all pending changes.
    /// </summary>
    /// <param name="fileSystem">Mutable FAT filesystem to serialize.</param>
    /// <param name="diskImage">Target disk image buffer to write to.</param>
    public void Serialize(MutableFatFileSystem fileSystem, byte[] diskImage)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(diskImage);

        fileSystem.CommitChanges(diskImage);
    }
}
