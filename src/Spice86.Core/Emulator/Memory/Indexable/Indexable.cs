namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Errors;

using System.Text;

/// <inheritdoc/>
public abstract class Indexable : IIndexable {
    /// <summary>
    ///     Allows indexed byte access to the memory.
    /// </summary>
    public abstract UInt8Indexer UInt8 {
        get;
    }

    /// <summary>
    ///     Allows indexed word access to the memory.
    /// </summary>
    public abstract UInt16Indexer UInt16 {
        get;
    }
    
    /// <summary>
    ///     Allows indexed big endian word access to the memory.
    /// </summary>
    public abstract UInt16BigEndianIndexer UInt16BigEndian {
        get;
    }


    /// <summary>
    ///     Allows indexed double word access to the memory.
    /// </summary>
    public abstract UInt32Indexer UInt32 {
        get;
    }

    /// <summary>
    ///     Allows indexed signed byte access to the memory.
    /// </summary>
    public abstract Int8Indexer Int8 {
        get;
    }

    /// <summary>
    ///     Allows indexed signed word access to the memory.
    /// </summary>
    public abstract Int16Indexer Int16 {
        get;
    }

    /// <summary>
    ///     Allows indexed signed double word access to the memory.
    /// </summary>
    public abstract Int32Indexer Int32 {
        get;
    }

    /// <summary>
    ///     Allows indexed 16 bit Offset / Segment access to the memory as SegmentedAddress Object.
    /// </summary>
    public abstract SegmentedAddressIndexer SegmentedAddress {
        get;
    }
    
    internal static (UInt8Indexer, UInt16Indexer, UInt16BigEndianIndexer, UInt32Indexer, Int8Indexer, Int16Indexer, Int32Indexer, SegmentedAddressIndexer) InstantiateIndexersFromByteReaderWriter(
            IByteReaderWriter byteReaderWriter) {
        UInt8Indexer uInt8 = new UInt8Indexer(byteReaderWriter);
        UInt16Indexer uInt16 = new UInt16Indexer(byteReaderWriter);
        UInt16BigEndianIndexer uInt16BigEndian = new UInt16BigEndianIndexer(byteReaderWriter);
        UInt32Indexer uInt32 = new UInt32Indexer(byteReaderWriter);
        Int8Indexer int8 = new Int8Indexer(uInt8);
        Int16Indexer int16 = new Int16Indexer(uInt16);
        Int32Indexer int32 = new Int32Indexer(uInt32);
        SegmentedAddressIndexer segmentedAddressIndexer = new SegmentedAddressIndexer(uInt16);
        return (uInt8, uInt16, uInt16BigEndian, uInt32, int8, int16, int32, segmentedAddressIndexer);
    }

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns>The zero-terminated string retrieved from memory.</returns>
    public virtual string GetZeroTerminatedString(uint address, int maxLength) {
        StringBuilder res = new();
        for (int i = 0; i < maxLength; i++) {
            byte characterByte = UInt8[(uint)(address + i)];
            if (characterByte == 0) {
                break;
            }

            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }

        return res.ToString();
    }

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string</param>
    /// <param name="value">The string to write</param>
    /// <param name="maxLength">The maximum length to write</param>
    /// <exception cref="UnrecoverableException"></exception>
    public virtual void SetZeroTerminatedString(uint address, string value, int maxLength) {
        if (value.Length + 1 > maxLength && !string.IsNullOrEmpty(value)) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        int i = 0;
        for (; i < value.Length; i++) {
            char character = value[i];
            byte charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            UInt8[(uint)(address + i)] = charFirstByte;
        }

        UInt8[(uint)(address + i)] = 0;
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    /// <param name="length">How many bytes to read from the byte array</param>
    public void LoadData(uint address, byte[] data, int length) {
        for (int i = 0; i < length; i++) {
            UInt8[(uint)(address + i)] = data[i];
        }
    }

    /// <summary>
    ///     Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    public void LoadData(uint address, ushort[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    /// <param name="length">How many words to read from the byte array</param>
    public void LoadData(uint address, ushort[] data, int length) {
        for (int i = 0; i < length; i++) {
            UInt16[(uint)(address + i)] = data[i];
        }
    }

    /// <summary>
    ///     Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from</param>
    /// <param name="destinationAddress">The address in memory to start writing to</param>
    /// <param name="length">How many bytes to copy</param>
    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        for (int i = 0; i < length; i++) {
            UInt8[(uint)(destinationAddress + i)] = UInt8[(uint)(sourceAddress + i)];
        }
    }

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The byte value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset8(uint address, byte value, uint amount) {
        for (int i = 0; i < amount; i++) {
            UInt8[(uint)(address + i)] = value;
        }
    }

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The ushort value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset16(uint address, ushort value, uint amount) {
        for (int i = 0; i < amount; i += 2) {
            UInt16[(uint)(address + i)] = value;
        }
    }


    /// <summary>
    /// Returns an array of bytes read from RAM.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of bytes, read from RAM.</returns>
    public byte[] GetData(uint address, uint length) {
        byte[] data = new byte[length];
        for (uint i = 0; i < length; i++) {
            data[i] = UInt8[address + i];
        }

        return data;
    }
}