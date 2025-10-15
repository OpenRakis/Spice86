namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

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
    public abstract SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <summary>
    ///     Allows indexed 32 bit Offset / Segment access to the memory as SegmentedAddress Object.
    /// </summary>
    public abstract SegmentedAddress32Indexer SegmentedAddress32 {
        get;
    }
    
    internal static (UInt8Indexer, UInt16Indexer, UInt16BigEndianIndexer, UInt32Indexer, Int8Indexer, Int16Indexer, Int32Indexer, SegmentedAddress16Indexer, SegmentedAddress32Indexer) InstantiateIndexersFromByteReaderWriter(
            IByteReaderWriter byteReaderWriter) {
        UInt8Indexer uInt8 = new UInt8Indexer(byteReaderWriter);
        UInt16Indexer uInt16 = new UInt16Indexer(uInt8);
        UInt16BigEndianIndexer uInt16BigEndian = new UInt16BigEndianIndexer(uInt8);
        UInt32Indexer uInt32 = new UInt32Indexer(uInt8);
        Int8Indexer int8 = new Int8Indexer(uInt8);
        Int16Indexer int16 = new Int16Indexer(uInt16);
        Int32Indexer int32 = new Int32Indexer(uInt32);
        SegmentedAddress16Indexer segmentedAddress16Indexer = new SegmentedAddress16Indexer(uInt16);
        SegmentedAddress32Indexer segmentedAddress32Indexer = new(uInt16, uInt32);
        return (uInt8, uInt16, uInt16BigEndian, uInt32, int8, int16, int32, segmentedAddress16Indexer, segmentedAddress32Indexer);
    }

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns>The zero-terminated string retrieved from memory.</returns>
    public virtual string GetZeroTerminatedString(SegmentedAddress address, int maxLength) {
        StringBuilder res = new();
        for (ushort i = 0; i < maxLength; i++) {
            byte characterByte = UInt8[address.PlusOffset(i)];
            if (characterByte == 0) {
                break;
            }

            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }

        return res.ToString();
    }

    /// <inheritdoc cref=""/>
    public virtual string GetZeroTerminatedString(uint address, int maxLength) {
        StringBuilder res = new();
        for (ushort i = 0; i < maxLength; i++) {
            byte characterByte = UInt8[address + i];
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
    public virtual void SetZeroTerminatedString(SegmentedAddress address, string value, int maxLength) {
        if (value.Length + 1 > maxLength && !string.IsNullOrEmpty(value)) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        ushort i = 0;
        Span<byte> charBytes = Encoding.ASCII.GetBytes(value);
        for (; i < charBytes.Length; i++) {
            byte character = charBytes[i];
            UInt8[address.PlusOffset(i)] = character;
        }

        UInt8[address.PlusOffset(i)] = 0;
    }
    
    public virtual void SetZeroTerminatedString(uint address, string value, int maxLength) {
        if (value.Length + 1 > maxLength && !string.IsNullOrEmpty(value)) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        ushort i = 0;
        Span<byte> charBytes = Encoding.ASCII.GetBytes(value);
        for (; i < charBytes.Length; i++) {
            byte character = charBytes[i];
            UInt8[address +i] = character;
        }

        UInt8[address + i] = 0;
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    public void LoadData(SegmentedAddress address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    /// <param name="length">How many bytes to read from the byte array</param>
    public void LoadData(SegmentedAddress address, byte[] data, int length) {
        for (int i = 0; i < length; i++) {
            UInt8[address.PlusOffset((ushort)i)] = data[i];
        }
    }

    public void LoadData(uint linearAddress, byte[] data, int length) {
        for (uint i = 0; i < length; i++) {
            UInt8[linearAddress + i] = data[i];
        }
    }
    /// <summary>
    ///     Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    public void LoadData(SegmentedAddress address, ushort[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    /// <param name="length">How many words to read from the byte array</param>
    public void LoadData(SegmentedAddress address, ushort[] data, int length) {
        for (ushort i = 0; i < length; i++) {
            UInt16[address.PlusOffset(i)] = data[i];
        }
    }

    /// <summary>
    ///     Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from</param>
    /// <param name="destinationAddress">The address in memory to start writing to</param>
    /// <param name="length">How many bytes to copy</param>
    public void MemCopy(SegmentedAddress sourceAddress, SegmentedAddress destinationAddress, int length) {
        for (ushort i = 0; i < length; i++) {
            UInt8[destinationAddress.PlusOffset(i)] = UInt8[sourceAddress.PlusOffset(i)];
        }
    }

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The byte value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset8(SegmentedAddress address, byte value, int amount) {
        for (ushort i = 0; i < amount; i++) {
            UInt8[address.PlusOffset(i)] = value;
        }
    }

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The ushort value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset16(SegmentedAddress address, ushort value, int amount) {
        for (ushort i = 0; i < amount; i += 2) {
            UInt16[address.PlusOffset(i)] = value;
        }
    }


    /// <summary>
    /// Returns an array of bytes read from RAM.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of bytes, read from RAM.</returns>
    public byte[] GetData(SegmentedAddress address, int length) {
        byte[] data = new byte[length];
        for (ushort i = 0; i < length; i++) {
            data[i] = UInt8[address.PlusOffset(i)];
        }

        return data;
    }
}