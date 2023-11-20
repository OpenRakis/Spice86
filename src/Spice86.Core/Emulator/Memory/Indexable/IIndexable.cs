namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Errors;

/// <summary>
/// Base class for objects that allow access to their data by index.
/// Various data types are supported: <br/>
///  - byte <br/>
///  - ushort <br/>
///  - uint <br/>
///  - segmented address tuple <br/>
///  - segmented address <br/>
/// </summary>
public interface IIndexable {
    /// <summary>
    ///     Allows indexed byte access to the memory.
    /// </summary>
    UInt8Indexer UInt8 {
        get;
    }

    /// <summary>
    ///     Allows indexed word access to the memory.
    /// </summary>
    UInt16Indexer UInt16 {
        get;
    }

    /// <summary>
    ///     Allows indexed double word access to the memory.
    /// </summary>
    UInt32Indexer UInt32 {
        get;
    }
    
    /// <summary>
    ///     Allows indexed signed byte access to the memory.
    /// </summary>
    Int8Indexer Int8 {
        get;
    }

    /// <summary>
    ///     Allows indexed signed word access to the memory.
    /// </summary>
    Int16Indexer Int16 {
        get;
    }

    /// <summary>
    ///     Allows indexed signed double word access to the memory.
    /// </summary>
    Int32Indexer Int32 {
        get;
    }

    /// <summary>
    ///     Allows indexed 16 bit Offset / Segment access to the memory as SegmentedAddress Object.
    /// </summary>
    SegmentedAddressIndexer SegmentedAddress {
        get;
    }

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns></returns>
    string GetZeroTerminatedString(uint address, int maxLength);

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string</param>
    /// <param name="value">The string to write</param>
    /// <param name="maxLength">The maximum length to write</param>
    /// <exception cref="UnrecoverableException"></exception>
    void SetZeroTerminatedString(uint address, string value, int maxLength);
    
    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    void LoadData(uint address, byte[] data);

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    /// <param name="length">How many bytes to read from the byte array</param>
    void LoadData(uint address, byte[] data, int length);

    /// <summary>
    ///     Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    void LoadData(uint address, ushort[] data);

    /// <summary>
    ///     Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    /// <param name="length">How many words to read from the byte array</param>
    void LoadData(uint address, ushort[] data, int length);

    /// <summary>
    ///     Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from</param>
    /// <param name="destinationAddress">The address in memory to start writing to</param>
    /// <param name="length">How many bytes to copy</param>
    void MemCopy(uint sourceAddress, uint destinationAddress, uint length);

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The byte value to write</param>
    /// <param name="amount">How many times to write the value</param>
    void Memset8(uint address, byte value, uint amount);

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The ushort value to write</param>
    /// <param name="amount">How many times to write the value</param>
    void Memset16(uint address, ushort value, uint amount);

    /// <summary>
    /// Returns an array of bytes read from RAM.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of bytes, read from RAM.</returns>
    byte[] GetData(uint address, uint length);
}