namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Provides indexed unsigned 16-byte access over memory.
/// </summary>
public sealed class UInt16Indexer : MemoryIndexer<ushort> {
    internal IByteReaderWriter ByteReaderWriter { get; }

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16Indexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt16Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, sizeof(ushort)) {
        ByteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override ushort this[uint address] {
        get => ReadValueCore(address);
        set => WriteValueCore(address, value);
    }

    /// <inheritdoc />
    protected internal override ushort ReadSegmented(ushort segment, uint offset) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort), out uint address)) {
            return ReadValueCore(address);
        } else {
            uint address1 = Mmu.TranslateAddress(segment, offset);
            uint address2 = Mmu.TranslateAddress(segment, offset + 1);
            return (ushort)(ByteReaderWriter[address1] | (ByteReaderWriter[address2] << 8));
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, ushort value) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort), out uint address)) {
            WriteValueCore(address, value);
        } else {
            uint address1 = Mmu.TranslateAddress(segment, offset);
            uint address2 = Mmu.TranslateAddress(segment, offset + 1);
            ByteReaderWriter[address1] = (byte)value;
            ByteReaderWriter[address2] = (byte)(value >>> 8);
        }
    }

    /// <inheritdoc/>
    public override int Count => ByteReaderWriter.Length / sizeof(ushort);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort ReadValueCore(uint address) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(ushort), out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= sizeof(ushort)) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return (ushort)(ByteReaderWriter[address] | (ByteReaderWriter[address + 1] << 8));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, ushort value) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(ushort), out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= sizeof(ushort)) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            ByteReaderWriter[address] = (byte)value;
            ByteReaderWriter[address + 1] = (byte)(value >>> 8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadValueUnsafe(ref byte source) => BitConverter.IsLittleEndian
        ? Unsafe.ReadUnaligned<ushort>(ref source)
        : BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<ushort>(ref source));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValueUnsafe(ref byte destination, ushort value) {
        if (BitConverter.IsLittleEndian) {
            Unsafe.WriteUnaligned(ref destination, value);
        } else {
            Unsafe.WriteUnaligned(ref destination, BinaryPrimitives.ReverseEndianness(value));
        }
    }
}
