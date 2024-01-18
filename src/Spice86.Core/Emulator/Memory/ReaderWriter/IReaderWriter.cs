namespace Spice86.Core.Emulator.Memory.ReaderWriter; 

/// <summary>
/// Interface for objects that allow to read at specific addresses
/// </summary>
public interface IReaderWriter<T> {
    /// <summary>
    /// Provides read / write access at address
    /// </summary>
    /// <param name="address">Address where to perform the operation</param>
    public T this[uint address] { get; set; }
    
    /// <summary>
    /// Length of the address space
    /// </summary>
    public uint Length { get; }
}