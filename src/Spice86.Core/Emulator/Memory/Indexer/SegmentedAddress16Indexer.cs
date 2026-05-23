namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// <para>Retrieves Segment / Offset pairs stored in Memory.</para>
/// <para>
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Instantiates objects of type SegmentedAddress for the return address.
/// </para>
/// </summary>
public class SegmentedAddress16Indexer : MemoryIndexer<SegmentedAddress> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-byte integer access over memory.</param>
    /// <param name="mmu">The MMU for access checks.</param>
    public SegmentedAddress16Indexer(UInt16Indexer uInt16Indexer, IMmu mmu) : base(mmu, sizeof(ushort) * 2) {
        _uInt16Indexer = uInt16Indexer;
    }

    internal IByteReaderWriter ByteReaderWriter => _uInt16Indexer.ByteReaderWriter;

    /// <inheritdoc/>
    public override SegmentedAddress this[uint address] {
        get => ReadValueCore(address);
        set => WriteValueCore(address, value);
    }

    /// <inheritdoc />
    protected internal override SegmentedAddress ReadSegmented(ushort segment, uint offset) {
        if (_uInt16Indexer.Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort) * 2, out uint address)) {
            return ReadValueCore(address);
        } else {
            ushort offsetValue = _uInt16Indexer.ReadSegmented(segment, offset);
            ushort segmentValue = _uInt16Indexer.ReadSegmented(segment, offset + sizeof(ushort));
            return new(segmentValue, offsetValue);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, SegmentedAddress value) {
        if (_uInt16Indexer.Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort) * 2, out uint address)) {
            WriteValueCore(address, value);
        } else {
            _uInt16Indexer.WriteSegmented(segment, offset, value.Offset);
            _uInt16Indexer.WriteSegmented(segment, offset + sizeof(ushort), value.Segment);
        }
    }

    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SegmentedAddress ReadValueCore(uint address) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(ushort) * 2, out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= sizeof(ushort) * 2) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return new(_uInt16Indexer[address + sizeof(ushort)], _uInt16Indexer[address]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, SegmentedAddress value) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(ushort) * 2, out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= sizeof(ushort) * 2) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            _uInt16Indexer[address] = value.Offset;
            _uInt16Indexer[address + sizeof(ushort)] = value.Segment;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SegmentedAddress ReadValueUnsafe(ref byte source) {
        ushort offset = Unsafe.ReadUnaligned<ushort>(ref source);
        ushort segment = Unsafe.ReadUnaligned<ushort>(ref Unsafe.AddByteOffset(ref source, sizeof(ushort)));
        if (!BitConverter.IsLittleEndian) {
            offset = BinaryPrimitives.ReverseEndianness(offset);
            segment = BinaryPrimitives.ReverseEndianness(segment);
        }

        return new(segment, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValueUnsafe(ref byte destination, SegmentedAddress value) {
        ushort offset = value.Offset;
        ushort segment = value.Segment;
        if (!BitConverter.IsLittleEndian) {
            offset = BinaryPrimitives.ReverseEndianness(offset);
            segment = BinaryPrimitives.ReverseEndianness(segment);
        }

        Unsafe.WriteUnaligned(ref destination, offset);
        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref destination, sizeof(ushort)), segment);
    }
}
