namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public sealed class UInt32Indexer : MemoryIndexer<uint> {
    internal IByteReaderWriter ByteReaderWriter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt32Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, sizeof(uint)) {
        ByteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override uint this[uint address] {
        get => ReadValueCore(address);
        set => WriteValueCore(address, value);
    }

    /// <inheritdoc />
    protected internal override uint ReadSegmented(ushort segment, uint offset) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(uint), out uint address)) {
            return ReadValueCore(address);
        } else {
            uint address1 = Mmu.TranslateAddress(segment, offset);
            uint address2 = Mmu.TranslateAddress(segment, offset + 1);
            uint address3 = Mmu.TranslateAddress(segment, offset + 2);
            uint address4 = Mmu.TranslateAddress(segment, offset + 3);
            return ByteReaderWriter[address1]
                 | ((uint)ByteReaderWriter[address2] << 8)
                 | ((uint)ByteReaderWriter[address3] << 16)
                 | ((uint)ByteReaderWriter[address4] << 24);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, uint value) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(uint), out uint address)) {
            WriteValueCore(address, value);
        } else {
            uint address1 = Mmu.TranslateAddress(segment, offset);
            uint address2 = Mmu.TranslateAddress(segment, offset + 1);
            uint address3 = Mmu.TranslateAddress(segment, offset + 2);
            uint address4 = Mmu.TranslateAddress(segment, offset + 3);
            ByteReaderWriter[address1] = (byte)value;
            ByteReaderWriter[address2] = (byte)(value >>> 8);
            ByteReaderWriter[address3] = (byte)(value >>> 16);
            ByteReaderWriter[address4] = (byte)(value >>> 24);
        }
    }

    /// <inheritdoc/>
    public override int Count => ByteReaderWriter.Length / sizeof(uint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadValueCore(uint address) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(uint), out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= sizeof(uint)) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return ByteReaderWriter[address]
                 | ((uint)ByteReaderWriter[address + 1] << 8)
                 | ((uint)ByteReaderWriter[address + 2] << 16)
                 | ((uint)ByteReaderWriter[address + 3] << 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, uint value) {
        if (ByteReaderWriter.TryGetSpan(address, sizeof(uint), out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= sizeof(uint)) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            ByteReaderWriter[address] = (byte)value;
            ByteReaderWriter[address + 1] = (byte)(value >>> 8);
            ByteReaderWriter[address + 2] = (byte)(value >>> 16);
            ByteReaderWriter[address + 3] = (byte)(value >>> 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadValueUnsafe(ref byte source) => BitConverter.IsLittleEndian
        ? Unsafe.ReadUnaligned<uint>(ref source)
        : BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<uint>(ref source));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValueUnsafe(ref byte destination, uint value) {
        if (BitConverter.IsLittleEndian) {
            Unsafe.WriteUnaligned(ref destination, value);
        } else {
            Unsafe.WriteUnaligned(ref destination, BinaryPrimitives.ReverseEndianness(value));
        }
    }
}
