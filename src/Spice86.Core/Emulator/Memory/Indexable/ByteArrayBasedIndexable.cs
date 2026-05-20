namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Errors;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Implementation of Indexable over a byte array.
/// </summary>
public class ByteArrayBasedIndexable : Indexable {

    /// <summary>
    /// Access to underlying ReaderWriter
    /// </summary>
    public ByteArrayReaderWriter ReaderWriter { get; }

    /// <summary>
    /// Underlying array being wrapped
    /// </summary>
    public byte[] Array { get => ReaderWriter.Array; }

    /// <inheritdoc/>
    public override UInt8Indexer UInt8 { get; }

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 { get; }

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 { get; }

    /// <inheritdoc/>
    public override Int8Indexer Int8 { get; }

    /// <inheritdoc/>
    public override Int16Indexer Int16 { get; }

    /// <inheritdoc/>
    public override UInt16BigEndianIndexer UInt16BigEndian { get; }
    /// <inheritdoc/>
    public override Int32Indexer Int32 { get; }

    /// <inheritdoc/>
    public override SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <inheritdoc/>
    public override SegmentedAddress32Indexer SegmentedAddress32 {
        get;
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="array">The byte array used as RAM storage.</param>
    public ByteArrayBasedIndexable(byte[] array) {
        ReaderWriter = new ByteArrayReaderWriter(array);
        (UInt8, UInt16, UInt16BigEndian, UInt32, Int8, Int16, Int32, SegmentedAddress16, SegmentedAddress32) =
            InstantiateIndexersFromByteReaderWriter(ReaderWriter, new RealModeMmu8086());
    }

    /// <inheritdoc/>
    public override string GetZeroTerminatedString(uint address, int maxLength) {
        if (ReaderWriter.TryGetSpan(address, maxLength, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
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
        int valueByteLength = Encoding.Latin1.GetByteCount(value) + 1;
        if (maxLength == 0) {
            maxLength = valueByteLength;
        } else if (maxLength < valueByteLength) {
            throw new UnrecoverableException(
                $"String {value} is more than {maxLength} cannot write it at offset {address}");
        }

        if (ReaderWriter.TryGetSpan(address, valueByteLength, out Span<byte> span, MemoryAccess.Write) &&
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
        if (ReaderWriter.TryGetSpan(address, length, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
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
        if (ReaderWriter.TryGetSpan(address, length, out Span<byte> span, MemoryAccess.Write) &&
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
        if (ReaderWriter.TryGetSpan(address, data.Length, out Span<byte> writeSpan, MemoryAccess.Write) &&
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
            if (ReaderWriter.TryGetSpan(address, byteCount, out Span<byte> writeSpan, MemoryAccess.Write) &&
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
        if (byteCount >= 0 &&
                ReaderWriter.TryGetSpan(address, byteCount, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= byteCount) {
            return span[..byteCount].ToArray();
        }

        return base.GetData(address, length);
    }

    /// <inheritdoc/>
    public override void GetData(uint address, Span<byte> data) {
        if (ReaderWriter.TryGetSpan(address, data.Length, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= data.Length) {
            span[..data.Length].CopyTo(data);
            return;
        }

        base.GetData(address, data);
    }

    /// <inheritdoc/>
    public override void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        int byteCount = (int)length;
        if (byteCount >= 0 &&
                ReaderWriter.TryGetSpan(sourceAddress, byteCount, out ReadOnlySpan<byte> src, MemoryAccess.Read) &&
                src.Length >= byteCount &&
                ReaderWriter.TryGetSpan(destinationAddress, byteCount, out Span<byte> dst, MemoryAccess.Write) &&
                dst.Length >= byteCount) {
            src[..byteCount].CopyTo(dst);
            return;
        }

        base.MemCopy(sourceAddress, destinationAddress, length);
    }

    /// <inheritdoc/>
    public override void Memset8(uint address, byte value, uint amount) {
        int byteCount = (int)amount;
        if (byteCount >= 0 && ReaderWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= byteCount) {
            // TODO: Determine whether it's advantageous to use Span<T>.Clear() when value is 0.
            span[..byteCount].Fill(value);
            return;
        }

        base.Memset8(address, value, amount);
    }

    /// <inheritdoc/>
    public override void Memset16(uint address, ushort value, uint amount) {
        // Make sure converting element count into byte count will not overflow for span-optimized path.
        if (amount <= int.MaxValue / sizeof(ushort)) {
            int byteCount = (int)amount * sizeof(ushort);
            if (byteCount >= 0 &&
                    ReaderWriter.TryGetSpan(address, byteCount, out Span<byte> span, MemoryAccess.Write) &&
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

        base.Memset16(address, value, amount);
    }
}
