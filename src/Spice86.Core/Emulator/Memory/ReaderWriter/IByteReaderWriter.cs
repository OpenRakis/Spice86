namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Interface for objects that allow to read and write bytes at specific addresses
/// </summary>
public interface IByteReaderWriter {
    /// <summary>
    /// Provides read / write access for bytes at address
    /// </summary>
    /// <param name="address">Address where to perform the operation</param>
    public byte this[uint address] { get; set; }
    
    /// <summary>
    /// Length of the address space
    /// </summary>
    public uint Length { get; }

}