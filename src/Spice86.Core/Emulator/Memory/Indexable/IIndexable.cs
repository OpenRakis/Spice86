namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Errors;

using System.Buffers;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Base class for objects that allow access to their data by index.
/// Various data types are supported:
/// <list type="bullet">
/// <item>8-bit signed and unsigned integers (<see langword="byte"/> and <see langword="sbyte"/>)</item>
/// <item>16-bit signed and unsigned integers (<see langword="ushort"/> and <see langword="short"/>, also referred to as a "word")</item>
/// <item>32-bit signed and unsigned integers (<see langword="uint"/> and <see langword="int"/>, also referred to as a "double word" or "dword")</item>
/// <item>16-bit segmented address tuple</item>
/// <item>32-bit segmented address tuple</item>
/// </list>
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
    ///     Allows indexed big endian word access to the memory.
    /// </summary>
    UInt16BigEndianIndexer UInt16BigEndian {
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
    SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <summary>
    ///     Allows indexed 32 bit Offset / Segment access to the memory as SegmentedAddress Object.
    /// </summary>
    SegmentedAddress32Indexer SegmentedAddress32 {
        get;
    }

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read.</param>
    /// <param name="maxLength">The maximum string length.</param>
    /// <returns>A string retrieved from memory (without the terminating NUL character).</returns>
    /// <remarks>
    /// The string should be decoded using ISO 8859-1.
    /// </remarks>
    string GetZeroTerminatedString(uint address, int maxLength);

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string.</param>
    /// <param name="value">The string to write. An extra NUL character will always be appended at the end, even if the string already contains this character.</param>
    /// <param name="maxLength">The maximum length to write. If zero, then assumes a length of <paramref name="value"/> length + 1 (for the NUL character).</param>
    /// <exception cref="UnrecoverableException">Encoded string length exceeds <paramref name="maxLength"/>.</exception>
    /// <remarks>
    /// The string should be encoded as ISO 8859-1. All characters that are not Basic Latin or Latin-1 Supplement
    /// (greater than <c>U+00FF</c>) should be replaced with a question mark (<c>?</c>).
    /// </remarks>
    void SetZeroTerminatedString(uint address, string value, int maxLength = 0);

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string.</param>
    /// <param name="value">The string to write as a span of characters. An extra NUL character will always be appended at the end, even if this string already contains this character.</param>
    /// <param name="maxLength">The maximum length to write. If zero, then assumes a length of <paramref name="value"/> length + 1 (for the NUL character).</param>
    /// <exception cref="UnrecoverableException">Encoded string length exceeds <paramref name="maxLength"/>.</exception>
    /// <remarks>
    /// The string should be encoded as ISO 8859-1. All characters that are not Basic Latin or Latin-1 Supplement
    /// (greater than <c>U+00FF</c>) should be replaced with a question mark (<c>?</c>).
    /// </remarks>
    virtual void SetZeroTerminatedString(uint address, ReadOnlySpan<char> value, int maxLength = 0) {
        int encodedByteCount = Encoding.Latin1.GetByteCount(value) + 1;
        if (maxLength > 0 && maxLength < encodedByteCount) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        // Avoid allocating an string from the span and calling SetZeroTerminatedString, instead rent a temporary array
        // from the array pool and use LoadData instead.
        byte[] tempArray = ArrayPool<byte>.Shared.Rent(encodedByteCount + 1);
        try {
            int loadByteCount = Encoding.Latin1.GetBytes(value, tempArray.AsSpan(0, encodedByteCount - 1));
            tempArray[loadByteCount++] = 0;
            Debug.Assert(loadByteCount == encodedByteCount);
            LoadData(address, tempArray, loadByteCount);
        } finally {
            ArrayPool<byte>.Shared.Return(tempArray);
        }
    }

    /// <summary>
    /// Read a space-padded string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read.</param>
    /// <param name="length">The fixed length of the string field.</param>
    /// <returns>The space-padded string retrieved from memory, including trailing spaces.</returns>
    /// <remarks>
    /// The string should be decoded using ISO 8859-1.
    /// </remarks>
    string GetSpacePaddedString(uint address, int length);

    /// <summary>
    /// Write a space-padded string to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string.</param>
    /// <param name="value">The string value to write. This string should be truncated if its encoded byte length exceeds <paramref name="length"/>.</param>
    /// <param name="length">The fixed length of the string field.</param>
    /// <remarks>
    /// The string should be encoded as ISO 8859-1. All characters that are not Basic Latin or Latin-1 Supplement
    /// (greater than <c>U+00FF</c>) should be replaced with a question mark (<c>?</c>).
    /// </remarks>
    void SetSpacePaddedString(uint address, string value, int length);

    /// <summary>
    /// Write a space-padded string to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string.</param>
    /// <param name="value">The string to write as a span of characters. This string should be truncated if its encoded byte length exceeds <paramref name="length"/>.</param>
    /// <param name="length">The fixed length of the string field.</param>
    /// <remarks>
    /// The string should be encoded as ISO 8859-1. All characters that are not Basic Latin or Latin-1 Supplement
    /// (greater than <c>U+00FF</c>) should be replaced with a question mark (<c>?</c>).
    /// </remarks>
    virtual void SetSpacePaddedString(uint address, ReadOnlySpan<char> value, int length) {
        if (Encoding.Latin1.GetByteCount(value) > length) {
            // Assume that the Unicode replacement character always fits into a byte.
            value = value[..length];
            Debug.Assert(Encoding.Latin1.GetByteCount(value) == length);
        }

        // Avoid allocating an string from the span and calling SetSpacePaddedString, instead rent a temporary array
        // from the array pool and use LoadData instead.
        byte[] tempArray = ArrayPool<byte>.Shared.Rent(length);
        try {
            Span<byte> tempSpan = tempArray.AsSpan(0, length);
            int byteCount = Encoding.Latin1.GetBytes(value, tempSpan);
            tempSpan[byteCount..].Fill((byte)' ');
            LoadData(address, tempArray, length);
        } finally {
            ArrayPool<byte>.Shared.Return(tempArray);
        }
    }

    /// <summary>
    /// Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The array of bytes to write.</param>
    void LoadData(uint address, byte[] data);

    /// <summary>
    /// Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The array of bytes to write.</param>
    /// <param name="length">Number of bytes to read from the byte array.</param>
    /// <remarks>
    /// Implementation should clamp <paramref name="length"/> between 0 and <paramref name="data"/> length (inclusive)
    /// to avoid potential out of bounds errors.
    /// </remarks>
    void LoadData(uint address, byte[] data, int length);

    /// <summary>
    /// Load data from a span of bytes into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The span of bytes to write.</param>
    virtual void LoadData(uint address, ReadOnlySpan<byte> data) {
        byte[] tempArray = ArrayPool<byte>.Shared.Rent(data.Length);
        try {
            data.CopyTo(tempArray);
            LoadData(address, tempArray, data.Length);
        } finally {
            ArrayPool<byte>.Shared.Return(tempArray);
        }
    }

    /// <summary>
    /// Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The array of words to write.</param>
    void LoadData(uint address, ushort[] data);

    /// <summary>
    /// Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The array of words to write.</param>
    /// <param name="length">Number of words to read from the word array.</param>
    /// <remarks>
    /// Implementation should clamp <paramref name="length"/> between 0 and <paramref name="data"/> length (inclusive)
    /// to avoid potential out of bounds errors.
    /// </remarks>
    void LoadData(uint address, ushort[] data, int length);

    /// <summary>
    /// Load data from a span of words into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing.</param>
    /// <param name="data">The span of words to write.</param>
    virtual void LoadData(uint address, ReadOnlySpan<ushort> data) {
        ushort[] tempArray = ArrayPool<ushort>.Shared.Rent(data.Length);
        try {
            data.CopyTo(tempArray);
            LoadData(address, tempArray, data.Length);
        } finally {
            ArrayPool<ushort>.Shared.Return(tempArray);
        }
    }

    /// <summary>
    /// Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from.</param>
    /// <param name="destinationAddress">The address in memory to start writing to.</param>
    /// <param name="length">Number of bytes to copy.</param>
    /// <remarks>
    /// Implementation must be able to properly handle overlapping memory address ranges.
    /// </remarks>
    void MemCopy(uint sourceAddress, uint destinationAddress, uint length);

    /// <summary>
    /// Fill a range of memory with a byte value.
    /// </summary>
    /// <param name="address">The memory address to start writing to.</param>
    /// <param name="value">The byte value to write.</param>
    /// <param name="amount">Number of times to write the value.</param>
    void Memset8(uint address, byte value, uint amount);

    /// <summary>
    /// Fill a range of memory with a word value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The word value to write.</param>
    /// <param name="amount">Number of times to write the value.</param>
    void Memset16(uint address, ushort value, uint amount);

    /// <summary>
    /// Reads an array of bytes from memory.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array in bytes.</param>
    /// <returns>The array of bytes, read from memory.</returns>
    byte[] GetData(uint address, uint length);

    /// <summary>
    /// Reads data from memory into a span of bytes.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="data">The span of bytes to containing the read data.</param>
    virtual void GetData(uint address, Span<byte> data) {
        // There are two choices here: 1) call GetData and incur the additional heap allocation or 2) use the UInt8
        // indexer to retrieve the bytes one at a time. Since this method should always be implemented when this
        // interface is implemented, the default implementation will use the simpliest approach and call GetData with
        // an array result.
        byte[] array = GetData(address, (uint)data.Length);
        array.CopyTo(data);
    }
}
