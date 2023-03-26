namespace Spice86.Core.Emulator.Memory;

/// <summary>
///     Represents any device that can be mapped to memory.
/// </summary>
public interface IMemoryDevice {
    /// <summary>
    /// The array used for storage
    /// </summary>
    byte[] GetStorage();
    
    /// <summary>
    ///     The size of the device in bytes.
    /// </summary>
    uint Size {
        get;
    }

    /// <summary>
    ///     Read a byte from the device.
    /// </summary>
    /// <param name="address">The memory address to read from</param>
    /// <returns>The byte value stored at the specified location</returns>
    byte Read(uint address);

    /// <summary>
    ///     Write a byte to the device.
    /// </summary>
    /// <param name="address">The memory address to write to</param>
    /// <param name="value">The byte value to write</param>
    void Write(uint address, byte value);
    
    /// <summary>
    ///     Write a word (uint16) to the device.
    /// </summary>
    /// <param name="address">The memory address to write to</param>
    /// <param name="value">The uint16 value to write</param>
    void WriteWord(uint address, ushort value);

    
    /// <summary>
    ///     Write a double word (uint32) to the device.
    /// </summary>
    /// <param name="address">The memory address to write to</param>
    /// <param name="value">The uint32 value to write</param>
    void WriteDWord(uint address, uint value);

    
    /// <summary>
    /// Get a span of bytes from the device.
    /// </summary>
    /// <param name="address">The start address of the memory</param>
    /// <param name="length">The length of the span</param>
    /// <returns>A byte span</returns>
    Span<byte> GetSpan(int address, int length);
}