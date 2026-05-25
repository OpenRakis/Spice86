namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Errors;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <inheritdoc cref="IIndexable"/>
public abstract class Indexable : IIndexable {
    /// <summary>
    /// Last valid character value allowed in Latin-1 (ISO 8859-1) encoding.
    /// </summary>
    internal const char LastValidCharLatin1 = (char)byte.MaxValue;

    /// <summary>
    /// Replacement ASCII character to use when a Unicode character cannot be encoded.
    /// </summary>
    internal const char AsciiReplacementChar = '?';

    /// <summary>
    /// Disables all use of high-performance span accesses within <see cref="Indexable"/>.
    /// </summary>
    internal static bool DisableIndexableSpanAccess { get; set; } = false;

    /// <summary>
    /// Disables all use of high-performance span accesses within <see cref="MemoryIndexer{T}"/>.
    /// </summary>
    internal static bool DisableIndexerSpanAccess { get; set; } = false;

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
        // Return an empty string if the maximum length is out of range or zero.
        if (maxLength <= 0) {
            return string.Empty;
        }

        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            if (readerWriter.TryGetSpan(address, maxLength, out ReadOnlySpan<byte> span, MemoryAccess.Read)
                && span.Length >= maxLength) {
                span = span[..maxLength];
                int zeroIndex = span.IndexOf((byte)0);
                return Encoding.Latin1.GetString(zeroIndex >= 0 ? span[..zeroIndex] : span);
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the maximum length.
        char[] array = ArrayPool<char>.Shared.Rent(maxLength);
        try {
            int i = 0;
            for (; i < maxLength; i++) {
                byte characterByte = uInt8[address + (uint)i];
                if (characterByte == 0) {
                    break;
                }

                // Latin-1 character encoding directly maps the entire 8-bit Unicode range as-is with no translations.
                char character = (char)characterByte;
                array[i] = character;
            }

            return new string(array, 0, i);
        } finally {
            ArrayPool<char>.Shared.Return(array);
        }
    }

    /// <inheritdoc/>
    public void SetZeroTerminatedString(uint address, string value, int maxLength = 0) {
        SetZeroTerminatedString(address, value.AsSpan(), maxLength);
    }

    /// <inheritdoc/>
    public virtual void SetZeroTerminatedString(uint address, ReadOnlySpan<char> value, int maxLength = 0) {
        // Do nothing if maximum length is out of range.
        if (maxLength < 0) {
            return;
        }

        // Argument validation.
        int valueByteLength = Encoding.Latin1.GetByteCount(value) + 1;
        if (maxLength != 0 && maxLength < valueByteLength) {
            throw new UnrecoverableException(
                $"String \"{value}\" is more than {maxLength} cannot write it at offset {address}");
        }

        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            if (readerWriter.TryGetSpan(address, valueByteLength, out Span<byte> span, MemoryAccess.Write)
                && span.Length >= valueByteLength) {
                // TODO: What happens when UTF-16 surrogate sequences are used? Does it translate into one byte or two?
                int bytesWritten = Encoding.Latin1.GetBytes(value, span);
                span[bytesWritten] = 0;
                Debug.Assert(bytesWritten + 1 == valueByteLength);
                return;
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the maximum length.
        int i = 0;
        for (; i < value.Length; i++) {
            char c = value[i];
            if (c > LastValidCharLatin1) {
                // Use an ASCII-compatible replacement character for the Unicode character.
                c = AsciiReplacementChar;
            }

            // Latin-1 character encoding directly maps the entire 8-bit Unicode range as-is with no translations.
            // Characters outside this range are handled by the above check.
            uInt8[address + (uint)i] = (byte)c;
        }

        // Append the final NUL character.
        uInt8[address + (uint)i] = 0;
    }

    /// <inheritdoc/>
    public virtual string GetSpacePaddedString(uint address, int length) {
        // Return an empty string if the length is out of range or zero.
        if (length <= 0) {
            return string.Empty;
        }

        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            if (readerWriter.TryGetSpan(address, length, out ReadOnlySpan<byte> span, MemoryAccess.Read)
                && span.Length >= length) {
                return Encoding.Latin1.GetString(span[..length]);
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the maximum length.
        char[] array = ArrayPool<char>.Shared.Rent(length);
        try {
            for (int i = 0; i < length; i++) {
                byte characterByte = uInt8[address + (uint)i];

                // Latin-1 character encoding directly maps the entire 8-bit Unicode range as-is with no translations.
                char character = (char)characterByte;
                array[i] = character;
            }

            return new string(array, 0, length);
        } finally {
            ArrayPool<char>.Shared.Return(array);
        }
    }

    /// <inheritdoc/>
    public void SetSpacePaddedString(uint address, string value, int length) {
        SetSpacePaddedString(address, value.AsSpan(), length);
    }

    /// <inheritdoc/>
    public virtual void SetSpacePaddedString(uint address, ReadOnlySpan<char> value, int length) {
        // Truncate string if longer than padded length. This only works because Latin-1 is an SBCS encoding.
        if (value.Length > length) {
            value = value[..length];
        }

        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            if (readerWriter.TryGetSpan(address, length, out Span<byte> span, MemoryAccess.Write)
                && span.Length >= length) {
                span = span[..length];

                // Encode string and fill any remaining bytes with spaces.
                // TODO: What happens when UTF-16 surrogate sequences are used? Does it translate into one byte or two?
                int bytesWritten = Encoding.Latin1.GetBytes(value.Length > length ? value[..length] : value, span);
                span[bytesWritten..].Fill((byte)' ');
                return;
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the elements.
        int i = 0;
        for (; i < value.Length; i++) {
            char c = value[i];
            if (c > LastValidCharLatin1) {
                // Use an ASCII-compatible replacement character for the Unicode character.
                c = AsciiReplacementChar;
            }

            // Latin-1 character encoding directly maps the entire 8-bit Unicode range as-is with no translations.
            // Characters outside this range are handled by the above check.
            uInt8[address + (uint)i] = (byte)c;
        }

        // Fill any remaining memory with spaces.
        for (; i < length; i++) {
            uInt8[address + (uint)i] = (byte)' ';
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
        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess && TryLoadData(uInt8.ByteReaderWriter, address, data)) {
            return;
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the elements.
        for (int i = 0; i < data.Length; i++) {
            uInt8[address + (uint)i] = data[i];
        }

        static bool TryLoadData(IReaderWriter<byte> readerWriter, uint address, ReadOnlySpan<byte> data) {
            if (!readerWriter.TryGetSpan(address, data.Length, out Span<byte> writeSpan, MemoryAccess.Write)
                || writeSpan.Length < data.Length) {
                return false;
            }

            data.CopyTo(writeSpan);
            return true;
        }
    }

    /// <inheritdoc/>
    public void LoadData(uint address, ushort[] data) {
        LoadData(address, data.AsSpan());
    }

    /// <inheritdoc/>
    public void LoadData(uint address, ushort[] data, int length) {
        // Avoid throwing an exception if length is out of bounds.
        if (length > 0) {
            LoadData(address, data.AsSpan(0, Math.Min(data.Length, length)));
        }
    }

    /// <inheritdoc/>
    public virtual void LoadData(uint address, ReadOnlySpan<ushort> data) {
        UInt16Indexer uInt16 = UInt16;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess && TryLoadData(uInt16.ByteReaderWriter, address, data)) {
            return;
        }

        // Use slow path processing one word at a time.

        // NOTE: The address will wrap around if it overflows when adding the elements.
        for (int i = 0; i < data.Length; i++) {
            uInt16[address + (uint)i * sizeof(ushort)] = data[i];
        }

        static bool TryLoadData(IReaderWriter<byte> readerWriter, uint address, ReadOnlySpan<ushort> data) {
            // Make sure converting element count into byte count will not overflow a signed 32-bit integer.
            if (data.Length > int.MaxValue / sizeof(ushort)) {
                return false;
            }

            // Try to get a byte span.
            int byteCount = data.Length * sizeof(ushort);
            if (!readerWriter.TryGetSpan(address, byteCount, out Span<byte> writeSpan, MemoryAccess.Write)
                || writeSpan.Length < byteCount) {
                return false;
            }

            if (BitConverter.IsLittleEndian) {
                // Fast path can copy bytes directly into memory without endian swapping.
                MemoryMarshal.Cast<ushort, byte>(data).CopyTo(writeSpan);
            } else {
                // Slow path requires endian swapping every value written. It may be possible to vectorize this, but
                // big endian architectures are relatively rare in the .NET world at this time.
                Span<ushort> writeWords = MemoryMarshal.Cast<byte, ushort>(writeSpan);
                for (int i = 0; i < data.Length; i++) {
                    writeWords[i] = BinaryPrimitives.ReverseEndianness(data[i]);
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public virtual byte[] GetData(uint address, uint length) {
        // Optimize case when length is zero (avoid allocating an extra zero-length array).
        if (length == 0) {
            return [];
        }

        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            int byteCount = (int)length;
            if (byteCount >= 0
                && readerWriter.TryGetSpan(address, byteCount, out ReadOnlySpan<byte> span, MemoryAccess.Read)
                && span.Length >= byteCount) {
                return span[..byteCount].ToArray();
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the elements.
        byte[] data = new byte[length];
        for (uint i = 0; i < length; i++) {
            data[i] = uInt8[address + i];
        }

        return data;
    }

    /// <inheritdoc/>
    public virtual void GetData(uint address, Span<byte> data) {
        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            if (readerWriter.TryGetSpan(address, data.Length, out ReadOnlySpan<byte> span, MemoryAccess.Read)
                && span.Length >= data.Length) {
                span[..data.Length].CopyTo(data);
                return;
            }
        }

        // Use slow path processing one byte at a time.

        // NOTE: The address will wrap around if it overflows when adding the elements.
        for (int i = 0; i < data.Length; i++) {
            data[i] = uInt8[address + (uint)i];
        }
    }

    /// <inheritdoc/>
    public virtual void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            int byteCount = (int)length;
            if (byteCount >= 0
                && readerWriter.TryGetSpan(sourceAddress, byteCount, out ReadOnlySpan<byte> src, MemoryAccess.Read)
                && src.Length >= byteCount
                && readerWriter.TryGetSpan(destinationAddress, byteCount, out Span<byte> dst, MemoryAccess.Write)
                && dst.Length >= byteCount) {
                src[..byteCount].CopyTo(dst);
                return;
            }
        }

        // Use slow path processing one byte at a time.

        if (destinationAddress - sourceAddress < length) {
            // Source and destination memory overlaps and source address is less than destination address. Need to copy
            // elements in reverse to avoid memory corruption.
            for (long i = length - 1; i >= 0; i--) {
                uInt8[destinationAddress + (uint)i] = uInt8[sourceAddress + (uint)i];
            }
        } else {
            for (uint i = 0; i < length; i++) {
                uInt8[destinationAddress + i] = uInt8[sourceAddress + i];
            }
        }
    }

    /// <inheritdoc/>
    public virtual void Memset8(uint address, byte value, uint amount) {
        UInt8Indexer uInt8 = UInt8;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess) {
            IReaderWriter<byte> readerWriter = uInt8.ByteReaderWriter;
            int byteCount = (int)amount;
            if (byteCount >= 0
                && readerWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write)
                && span.Length >= byteCount) {
                // TODO: Determine whether it's advantageous to use Span<T>.Clear() when value is 0.
                span[..byteCount].Fill(value);
                return;
            }
        }

        // Use slow path processing one byte at a time.

        for (uint i = 0; i < amount; i++) {
            uInt8[address + i] = value;
        }
    }

    /// <inheritdoc/>
    public virtual void Memset16(uint address, ushort value, uint amount) {
        UInt16Indexer uInt16 = UInt16;

        // Try to use high performance span access if possible.
        if (!DisableIndexableSpanAccess && TryMemSet16(uInt16.ByteReaderWriter, address, value, amount)) {
            return;
        }

        // Use slow path processing one byte at a time.

        for (uint i = 0; i < amount; i++) {
            uInt16[address + i * sizeof(ushort)] = value;
        }

        static bool TryMemSet16(IReaderWriter<byte> readerWriter, uint address, ushort value, uint length) {
            // Optimize for zero length.
            if (length == 0) {
                return true;
            }

            // Make sure converting element count into byte count will not overflow for span-optimized path.
            if (length > int.MaxValue / sizeof(ushort)) {
                return false;
            }

            int byteCount = (int)length * sizeof(ushort);
            Debug.Assert(byteCount >= 0);

            if (!readerWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write)
                || span.Length < byteCount) {
                return false;
            }

            if (value == 0) {
                // Fast path for zeroing data.
                span[..byteCount].Clear();
            } else {
                if (!BitConverter.IsLittleEndian) {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }
                MemoryMarshal.Cast<byte, ushort>(span[..byteCount]).Fill(value);
            }

            return true;
        }
    }
}
