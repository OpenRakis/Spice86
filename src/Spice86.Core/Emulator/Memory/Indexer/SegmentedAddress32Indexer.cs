namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Indexable;
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
/// Instantiates objects of type SegmentedAddress32 preserving the full 32-bit offset.
/// </para>
/// <para>
/// A 32-bit far pointer is 6 bytes of data: 4-byte offset followed by 2-byte segment.
/// Hardware accesses this as two 4-byte pops (offset then padded segment), so segmented
/// access performs two separate 4-byte MMU checks with offset wrapping between them,
/// then delegates to the underlying UInt32 and UInt16 indexers for translation and I/O.
/// </para>
/// </summary>
public class SegmentedAddress32Indexer : MemoryIndexer<SegmentedAddress32> {
    private readonly UInt16Indexer _uInt16Indexer;
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-bit integer access over memory.</param>
    /// <param name="uInt32Indexer">The class that provides indexed unsigned 32-bit integer access over memory.</param>
    /// <param name="mmu">The MMU for access checks.</param>
    public SegmentedAddress32Indexer(UInt16Indexer uInt16Indexer, UInt32Indexer uInt32Indexer, IMmu mmu) : base(mmu, sizeof(uint) * 2) {
        _uInt16Indexer = uInt16Indexer;
        _uInt32Indexer = uInt32Indexer;
    }

    internal IByteReaderWriter ByteReaderWriter => _uInt16Indexer.ByteReaderWriter;

    /// <inheritdoc/>
    public override SegmentedAddress32 this[uint address] {
        get => ReadValueCore(address);
        set => WriteValueCore(address, value);
    }

    /// <summary>
    /// Performs two separate 4-byte MMU checks matching hardware's two-pop access pattern,
    /// then delegates to <see cref="ReadSegmented"/>/<see cref="WriteSegmented"/>.
    /// </summary>
    public override SegmentedAddress32 this[ushort segment, uint offset, SegmentAccessKind accessKind] {
        get {
            Mmu.CheckAccess(segment, offset, sizeof(uint), accessKind);
            // Cast to ushort: models SP register wrapping between the two hardware pops.
            // Each pop checks its own 4-byte span at the wrapped 16-bit offset.
            Mmu.CheckAccess(segment, (ushort)(offset + sizeof(uint)), sizeof(uint), accessKind);
            return ReadSegmented(segment, offset);
        }
        set {
            Mmu.CheckAccess(segment, offset, sizeof(uint), accessKind);
            Mmu.CheckAccess(segment, (ushort)(offset + sizeof(uint)), sizeof(uint), accessKind);
            WriteSegmented(segment, offset, value);
        }
    }

    /// <inheritdoc />
    protected internal override SegmentedAddress32 ReadSegmented(ushort segment, uint offset) {
        if (_uInt16Indexer.Mmu.TryTranslateAddressRange(segment, offset, sizeof(uint) * 2, out uint address)) {
            return ReadValueCore(address);
        } else {
            uint offsetValue = _uInt32Indexer.ReadSegmented(segment, offset);
            ushort segmentValue = _uInt16Indexer.ReadSegmented(segment, offset + sizeof(uint));
            return new(segmentValue, offsetValue);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, SegmentedAddress32 value) {
        if (_uInt16Indexer.Mmu.TryTranslateAddressRange(segment, offset, sizeof(uint) * 2, out uint address)) {
            WriteValueCore(address, value);
        } else {
            _uInt32Indexer.WriteSegmented(segment, offset, value.Offset);
            _uInt16Indexer.WriteSegmented(segment, offset + sizeof(uint), value.Segment);
        }
    }

    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SegmentedAddress32 ReadValueCore(uint address) {
        if (!Indexable.DisableSpanAccess
            && ByteReaderWriter.TryGetSpan(address, sizeof(uint) * 2, out ReadOnlySpan<byte> span, MemoryAccess.Read)
            && span.Length >= sizeof(uint) * 2) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return new(_uInt16Indexer[address + sizeof(uint)], _uInt32Indexer[address]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, SegmentedAddress32 value) {
        if (!Indexable.DisableSpanAccess
            && ByteReaderWriter.TryGetSpan(address, sizeof(uint) * 2, out Span<byte> span, MemoryAccess.Write)
            && span.Length >= sizeof(uint) * 2) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            _uInt32Indexer[address] = value.Offset;
            _uInt16Indexer[address + sizeof(uint)] = value.Segment;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SegmentedAddress32 ReadValueUnsafe(ref byte source) {
        uint offset = Unsafe.ReadUnaligned<uint>(ref source);
        ushort segment = Unsafe.ReadUnaligned<ushort>(ref Unsafe.AddByteOffset(ref source, sizeof(uint)));
        if (!BitConverter.IsLittleEndian) {
            offset = BinaryPrimitives.ReverseEndianness(offset);
            segment = BinaryPrimitives.ReverseEndianness(segment);
        }

        return new(segment, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValueUnsafe(ref byte destination, SegmentedAddress32 value) {
        uint offset = value.Offset;
        ushort segment = value.Segment;
        if (!BitConverter.IsLittleEndian) {
            offset = BinaryPrimitives.ReverseEndianness(offset);
            segment = BinaryPrimitives.ReverseEndianness(segment);
        }

        Unsafe.WriteUnaligned(ref destination, offset);
        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref destination, sizeof(uint)), segment);
    }
}
