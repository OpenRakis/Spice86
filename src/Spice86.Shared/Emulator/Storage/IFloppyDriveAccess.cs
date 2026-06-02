namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Provides low-level sector access to emulated floppy drives.
/// Used by the BIOS INT 13h handler to read and write sectors without any
/// dependency on the DOS layer.
/// </summary>
public interface IFloppyDriveAccess {
    /// <summary>
    /// Gets the geometry of the specified drive.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <returns>The geometry result object.</returns>
    FloppyGeometryResult GetGeometry(byte driveNumber);

    /// <summary>
    /// Reads bytes from a floppy image into a caller-supplied buffer.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imageByteOffset">Byte offset within the flat floppy image to start reading from.</param>
    /// <param name="destination">Buffer to receive the data.</param>
    /// <param name="destOffset">Offset within <paramref name="destination"/> to start writing.</param>
    /// <param name="byteCount">Number of bytes to read.</param>
    /// <returns>The transfer result object.</returns>
    FloppyTransferResult ReadFromImage(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset, int byteCount);

    /// <summary>
    /// Writes bytes from a caller-supplied buffer into a floppy image.
    /// </summary>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imageByteOffset">Byte offset within the flat floppy image to start writing to.</param>
    /// <param name="source">Buffer containing the data to write.</param>
    /// <param name="srcOffset">Offset within <paramref name="source"/> to start reading from.</param>
    /// <param name="byteCount">Number of bytes to write.</param>
    /// <returns>The transfer result object.</returns>
    FloppyTransferResult WriteToImage(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset, int byteCount);
}
