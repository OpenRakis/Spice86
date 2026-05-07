namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Abstracts raw byte access to a backing data store (file, memory buffer, etc.).</summary>
public interface IDataSource {
    /// <summary>Reads bytes from the source starting at <paramref name="byteOffset"/>.</summary>
    /// <param name="byteOffset">Zero-based byte position within the source.</param>
    /// <param name="destination">Buffer that receives the data.</param>
    /// <returns>Number of bytes actually read.</returns>
    int Read(long byteOffset, Span<byte> destination);

    /// <summary>Gets the total number of bytes available in this source.</summary>
    long LengthBytes { get; }
}
