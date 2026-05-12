namespace Spice86.Core.Emulator.Devices.Storage;

/// <summary>
/// Provides low-level sector access to emulated floppy drives.
/// Used by the BIOS INT 13h handler to read and write sectors without any
/// dependency on the DOS layer.
/// </summary>
public interface IFloppyDriveAccess {
    /// <summary>
    /// Returns the geometry of the specified drive.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="totalCylinders">Receives the total number of cylinders (tracks per side).</param>
    /// <param name="headsPerCylinder">Receives the number of heads (sides).</param>
    /// <param name="sectorsPerTrack">Receives the number of sectors per track.</param>
    /// <param name="bytesPerSector">Receives the number of bytes per sector.</param>
    /// <returns><see langword="true"/> when the drive is present and has media; otherwise <see langword="false"/>.</returns>
    bool TryGetGeometry(byte driveNumber, out int totalCylinders, out int headsPerCylinder, out int sectorsPerTrack, out int bytesPerSector);

    /// <summary>
    /// Reads bytes from a floppy image into a caller-supplied buffer.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imageByteOffset">Byte offset within the flat floppy image to start reading from.</param>
    /// <param name="destination">Buffer to receive the data.</param>
    /// <param name="destOffset">Offset within <paramref name="destination"/> to start writing.</param>
    /// <param name="byteCount">Number of bytes to read.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> when the drive is not present or the offset is out of range.</returns>
    bool ReadFromImage(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset, int byteCount);

    /// <summary>
    /// Writes bytes from a caller-supplied buffer into a floppy image.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imageByteOffset">Byte offset within the flat floppy image to start writing to.</param>
    /// <param name="source">Buffer containing the data to write.</param>
    /// <param name="srcOffset">Offset within <paramref name="source"/> to start reading from.</param>
    /// <param name="byteCount">Number of bytes to write.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> when the drive is not present or the offset is out of range.</returns>
    bool WriteToImage(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset, int byteCount);
}
