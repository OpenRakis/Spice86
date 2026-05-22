namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Errors;

using System.Text;

/// <inheritdoc cref="IIndexable"/>
public abstract class Indexable : IIndexable {
    /// <inheritdoc/>
    public abstract UInt8Indexer UInt8 {
        get;
    }

    /// <inheritdoc/>
    public abstract UInt16Indexer UInt16 {
        get;
    }

    /// <inheritdoc/>
    public abstract UInt16BigEndianIndexer UInt16BigEndian {
        get;
    }

    /// <inheritdoc/>
    public abstract UInt32Indexer UInt32 {
        get;
    }

    /// <inheritdoc/>
    public abstract Int8Indexer Int8 {
        get;
    }

    /// <inheritdoc/>
    public abstract Int16Indexer Int16 {
        get;
    }

    /// <inheritdoc/>
    public abstract Int32Indexer Int32 {
        get;
    }

    /// <inheritdoc/>
    public abstract SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <inheritdoc/>
    public abstract SegmentedAddress32Indexer SegmentedAddress32 {
        get;
    }

    internal record struct InstantiatedIndexers(UInt8Indexer UInt8, UInt16Indexer UInt16,
        UInt16BigEndianIndexer UInt16BigEndian, UInt32Indexer UInt32, Int8Indexer Int8, Int16Indexer Int16,
        Int32Indexer Int32, SegmentedAddress16Indexer SegmentedAddress16,
        SegmentedAddress32Indexer SegmentedAddress32);

    internal static InstantiatedIndexers InstantiateIndexersFromByteReaderWriter(IByteReaderWriter byteReaderWriter, IMmu mmu) {
        UInt8Indexer uInt8 = new UInt8Indexer(byteReaderWriter, mmu);
        UInt16Indexer uInt16 = new UInt16Indexer(byteReaderWriter, mmu);
        UInt16BigEndianIndexer uInt16BigEndian = new UInt16BigEndianIndexer(byteReaderWriter, mmu);
        UInt32Indexer uInt32 = new UInt32Indexer(byteReaderWriter, mmu);
        Int8Indexer int8 = new Int8Indexer(uInt8, mmu);
        Int16Indexer int16 = new Int16Indexer(uInt16, mmu);
        Int32Indexer int32 = new Int32Indexer(uInt32, mmu);
        SegmentedAddress16Indexer segmentedAddress16Indexer = new SegmentedAddress16Indexer(uInt16, mmu);
        SegmentedAddress32Indexer segmentedAddress32Indexer = new SegmentedAddress32Indexer(uInt16, uInt32, mmu);
        return new(uInt8, uInt16, uInt16BigEndian, uInt32, int8, int16, int32, segmentedAddress16Indexer, segmentedAddress32Indexer);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public virtual void SetZeroTerminatedString(uint address, string value, int maxLength = 0) {
        SetZeroTerminatedString(address, value.AsSpan(), maxLength);
    }

    /// <inheritdoc/>
    public virtual void SetZeroTerminatedString(uint address, ReadOnlySpan<char> value, int maxLength = 0) {
        if (maxLength < 0) {
            return;
        }

        int valueByteLength = value.Length + 1;
        if (maxLength != 0 && valueByteLength > maxLength) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        int i = 0;
        for (; i < value.Length; i++) {
            char c = value[i];
            if (c >= '\u0100') {
                // Use an ASCII-compatible replacement character for the Unicode character.
                c = '?';
            }
            UInt8[(uint)(address + i)] = (byte)c;
        }

        UInt8[(uint)(address + i)] = 0;
    }

    /// <inheritdoc/>
    public virtual string GetSpacePaddedString(uint address, int length) {
        StringBuilder result = new(length);
        for (int i = 0; i < length; i++) {
            byte b = UInt8[address + (uint)i];
            result.Append((char)b);
        }
        return result.ToString();
    }

    /// <inheritdoc/>
    public virtual void SetSpacePaddedString(uint address, string value, int length) {
        SetSpacePaddedString(address, value.AsSpan(), length);
    }

    /// <inheritdoc/>
    public virtual void SetSpacePaddedString(uint address, ReadOnlySpan<char> value, int length) {
        if (value.Length > length) {
            value = value[..length];
        }

        int i = 0;
        for (; i < value.Length; i++) {
            char c = value[i];
            if (c >= '\u0100') {
                // Use an ASCII-compatible replacement character for the Unicode character.
                c = '?';
            }
            UInt8[address + (uint)i] = (byte)c;
        }
        for (; i < length; i++) {
            UInt8[address + (uint)i] = (byte)' ';
        }
    }

    /// <inheritdoc/>
    public void LoadData(uint address, byte[] data) {
        LoadData(address, data.AsSpan());
    }

    /// <inheritdoc/>
    public void LoadData(uint address, byte[] data, int length) {
        // Avoid throwing an exception if length is out of bounds.
        if (length > 0) {
            LoadData(address, data.AsSpan(0, Math.Min(data.Length, length)));
        }
    }

    /// <inheritdoc/>
    public virtual void LoadData(uint address, ReadOnlySpan<byte> data) {
        for (int i = 0; i < data.Length; i++) {
            UInt8[(uint)(address + i)] = data[i];
        }
    }

    /// <inheritdoc/>
    public void LoadData(uint address, ushort[] data) {
        LoadData(address, data.AsSpan());
    }

    /// <inheritdoc/>
    public void LoadData(uint address, ushort[] data, int length) {
        LoadData(address, data.AsSpan(0, length));
    }

    /// <inheritdoc/>
    public virtual void LoadData(uint address, ReadOnlySpan<ushort> data) {
        for (int i = 0; i < data.Length; i++) {
            UInt16[(uint)(address + i)] = data[i];
        }
    }

    /// <inheritdoc/>
    public virtual void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        if (destinationAddress - sourceAddress < length) {
            // Source and destination memory overlaps and source address is less than destination address. Need to copy
            // elements in reverse to avoid memory corruption.
            for (long i = length - 1; i >= 0; i--) {
                UInt8[destinationAddress + (uint)i] = UInt8[sourceAddress + (uint)i];
            }
        } else {
            for (uint i = 0; i < length; i++) {
                UInt8[destinationAddress + i] = UInt8[sourceAddress + i];
            }
        }
    }

    /// <inheritdoc/>
    public virtual void Memset8(uint address, byte value, uint amount) {
        for (int i = 0; i < amount; i++) {
            UInt8[(uint)(address + i)] = value;
        }
    }

    /// <inheritdoc/>
    public virtual void Memset16(uint address, ushort value, uint amount) {
        for (int i = 0; i < amount; i += 2) {
            UInt16[(uint)(address + i)] = value;
        }
    }

    /// <inheritdoc/>
    public virtual byte[] GetData(uint address, uint length) {
        byte[] data = new byte[length];
        for (uint i = 0; i < length; i++) {
            data[i] = UInt8[address + i];
        }

        return data;
    }

    /// <inheritdoc/>
    public virtual void GetData(uint address, Span<byte> data) {
        for (int i = 0; i < data.Length; i++) {
            data[i] = UInt8[address + i];
        }
    }
}
