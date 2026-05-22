namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Errors;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Provides a base class for memory-based data structures that have a base address.
/// </summary>
public abstract class AbstractMemoryBasedDataStructure : Indexable, IBaseAddressProvider {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    protected AbstractMemoryBasedDataStructure(IByteReaderWriter byteReaderWriter) {
        ByteReaderWriter = byteReaderWriter;
        ByteReaderWriterShiftedToBaseAddress = new ByteReaderWriterWithBaseAddress(byteReaderWriter, this);
        (UInt8, UInt16, UInt16BigEndian, UInt32, Int8, Int16, Int32, SegmentedAddress16, SegmentedAddress32) =
            InstantiateIndexersFromByteReaderWriter(ByteReaderWriterShiftedToBaseAddress, new RealModeMmu8086());
    }

    /// <inheritdoc/>
    public sealed override UInt8Indexer UInt8 {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt16Indexer UInt16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt16BigEndianIndexer UInt16BigEndian {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt32Indexer UInt32 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int8Indexer Int8 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int16Indexer Int16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int32Indexer Int32 {
        get;
    }

    /// <inheritdoc/>
    public sealed override SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override SegmentedAddress32Indexer SegmentedAddress32 {
        get;
    }

    /// <summary>
    /// Where data are from
    /// </summary>
    public IByteReaderWriter ByteReaderWriter { get; }

    /// <summary>
    /// Where data are from, shifted to base address.
    /// </summary>
    private ByteReaderWriterWithBaseAddress ByteReaderWriterShiftedToBaseAddress { get; }

    /// <summary>
    /// The base address of the data structure.
    /// </summary>
    public abstract uint BaseAddress { get; }

    /// <summary>
    /// Adds the base address to the specified offset.
    /// </summary>
    /// <param name="offset">The offset to add to the <see cref="BaseAddress"/></param>
    /// <returns>The linear address of <see cref="BaseAddress"/> + offset</returns>
    /// <remarks>
    /// If the destination address overflows, it will wrap around.
    /// </remarks>
    protected uint ComputeAddressFromOffset(uint offset) {
        return BaseAddress + offset;
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt8Array GetUInt8Array(uint start, int length) {
        return new UInt8Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets a uint16 array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint16 values.</returns>
    public UInt16Array GetUInt16Array(uint start, int length) {
        return new UInt16Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets an 32-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt32Array GetUInt32Array(uint start, int length) {
        return new UInt32Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets a SegmentedAddress array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public SegmentedAddressArray GetSegmentedAddressArray(uint start, int length) {
        return new SegmentedAddressArray(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <inheritdoc/>
    public override string GetZeroTerminatedString(uint address, int maxLength) {
        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, maxLength, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= maxLength) {
            span = span[..maxLength];
            // NOTE: Can't use IndexOf as an extension method, because CommunityToolkit.HighPerformance also implements
            // a similarly named & typed extension method, but it uses an "in" parameter instead. This "breaks" the
            // compiler's extension overload resolution algorithm.
            int zeroIndex = System.MemoryExtensions.IndexOf(span, (byte)0);
            return Encoding.Latin1.GetString(zeroIndex >= 0 ? span[..zeroIndex] : span);
        }

        return base.GetZeroTerminatedString(address, maxLength);
    }

    /// <inheritdoc/>
    public override void SetZeroTerminatedString(uint address, string value, int maxLength = 0) {
        SetZeroTerminatedString(address, value.AsSpan(), maxLength);
    }

    /// <inheritdoc/>
    public override void SetZeroTerminatedString(uint address, ReadOnlySpan<char> value, int maxLength = 0) {
        if (maxLength < 0) {
            return;
        }

        int valueByteLength = Encoding.Latin1.GetByteCount(value) + 1;
        if (maxLength != 0 && maxLength < valueByteLength) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, valueByteLength, out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= valueByteLength) {
            int bytesWritten = Encoding.Latin1.GetBytes(value, span);
            span[bytesWritten] = 0;
            Debug.Assert(bytesWritten + 1 == valueByteLength);
            return;
        }

        base.SetZeroTerminatedString(address, value, maxLength);
    }

    /// <inheritdoc/>
    public override string GetSpacePaddedString(uint address, int length) {
        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, length, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= length) {
            return Encoding.Latin1.GetString(span[..length]);
        }

        return base.GetSpacePaddedString(address, length);
    }

    /// <inheritdoc/>
    public override void SetSpacePaddedString(uint address, string value, int length) {
        SetSpacePaddedString(address, value.AsSpan(), length);
    }

    /// <inheritdoc/>
    public override void SetSpacePaddedString(uint address, ReadOnlySpan<char> value, int length) {
        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, length, out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= length) {
            span = span[..length];
            int bytesWritten = Encoding.Latin1.GetBytes(value, span);
            span[bytesWritten..].Fill((byte)' ');
            return;
        }

        base.SetSpacePaddedString(address, value, length);
    }

    /// <inheritdoc/>
    public override void LoadData(uint address, ReadOnlySpan<byte> data) {
        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, data.Length, out Span<byte> writeSpan, MemoryAccess.Write) &&
                writeSpan.Length >= data.Length) {
            data.CopyTo(writeSpan);
            return;
        }

        base.LoadData(address, data);
    }

    /// <inheritdoc/>
    public override void LoadData(uint address, ReadOnlySpan<ushort> data) {
        // Make sure converting element count into byte count will not overflow for span-optimized path.
        if (data.Length <= int.MaxValue / sizeof(ushort)) {
            int byteCount = data.Length * sizeof(ushort);
            ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
            if (readerWriter.TryGetSpan(address, byteCount, out Span<byte> writeSpan, MemoryAccess.Write) &&
                    writeSpan.Length >= byteCount) {
                if (BitConverter.IsLittleEndian) {
                    // Fast path can copy bytes directly into memory without endian swapping.
                    MemoryMarshal.Cast<ushort, byte>(data).CopyTo(writeSpan);
                } else {
                    // Slow path requires endian swapping every value written. It may be possible to vectorize this,
                    // but big endian architectures are relatively rare in the .NET world at this time.
                    Span<ushort> writeWords = MemoryMarshal.Cast<byte, ushort>(writeSpan);
                    for (int i = 0; i < data.Length; i++) {
                        writeWords[i] = BinaryPrimitives.ReverseEndianness(data[i]);
                    }
                }
                return;
            }
        }

        base.LoadData(address, data);
    }

    /// <inheritdoc/>
    public override byte[] GetData(uint address, uint length) {
        int byteCount = (int)length;
        if (byteCount >= 0) {
            ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
            if (readerWriter.TryGetSpan(address, byteCount, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                    span.Length >= byteCount) {
                return span[..byteCount].ToArray();
            }
        }

        return base.GetData(address, length);
    }

    /// <inheritdoc/>
    public override void GetData(uint address, Span<byte> data) {
        ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
        if (readerWriter.TryGetSpan(address, data.Length, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= data.Length) {
            span[..data.Length].CopyTo(data);
            return;
        }

        base.GetData(address, data);
    }

    /// <inheritdoc/>
    public override void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        int byteCount = (int)length;
        if (byteCount >= 0) {
            ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
            if (readerWriter.TryGetSpan(sourceAddress, byteCount, out ReadOnlySpan<byte> src, MemoryAccess.Read) &&
                    src.Length >= byteCount &&
                    readerWriter.TryGetSpan(destinationAddress, byteCount, out Span<byte> dst, MemoryAccess.Write) &&
                    dst.Length >= byteCount) {
                src[..byteCount].CopyTo(dst);
                return;
            }
        }

        base.MemCopy(sourceAddress, destinationAddress, length);
    }

    /// <inheritdoc/>
    public override void Memset8(uint address, byte value, uint amount) {
        int byteCount = (int)amount;
        if (byteCount >= 0) {
            ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
            if (readerWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write) &&
                    span.Length >= byteCount) {
                // TODO: Determine whether it's advantageous to use Span<T>.Clear() when value is 0.
                span[..byteCount].Fill(value);
                return;
            }
        }

        base.Memset8(address, value, amount);
    }

    /// <inheritdoc/>
    public override void Memset16(uint address, ushort value, uint amount) {
        // Make sure converting element count into byte count will not overflow for span-optimized path.
        if (amount <= int.MaxValue / sizeof(ushort)) {
            int byteCount = (int)amount * sizeof(ushort);
            if (byteCount >= 0) {
                ByteReaderWriterWithBaseAddress readerWriter = ByteReaderWriterShiftedToBaseAddress;
                if (readerWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write) &&
                        span.Length >= byteCount) {
                    if (value == 0) {
                        span[..byteCount].Clear();
                    } else {
                        if (!BitConverter.IsLittleEndian) {
                            value = BinaryPrimitives.ReverseEndianness(value);
                        }
                        MemoryMarshal.Cast<byte, ushort>(span[..byteCount]).Fill(value);
                    }
                    return;
                }
            }
        }

        base.Memset16(address, value, amount);
    }
}
