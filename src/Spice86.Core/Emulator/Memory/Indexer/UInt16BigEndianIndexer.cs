namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Provides indexed unsigned 16-byte big endian access over memory.
/// </summary>
public sealed class UInt16BigEndianIndexer : MemoryIndexer<ushort> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16BigEndianIndexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt16BigEndianIndexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, sizeof(ushort)) {
        _byteReaderWriter = byteReaderWriter;
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
            return (ushort)(_byteReaderWriter[address2] | ((uint)_byteReaderWriter[address1] << 8));
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, ushort value) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort), out uint address)) {
            WriteValueCore(address, value);
        } else {
            uint address1 = Mmu.TranslateAddress(segment, offset);
            uint address2 = Mmu.TranslateAddress(segment, offset + 1);
            _byteReaderWriter[address1] = (byte)(value >>> 8);
            _byteReaderWriter[address2] = (byte)value;
        }
    }

    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / sizeof(ushort);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort ReadValueCore(uint address) {
        if (!Indexable.DisableIndexerSpanAccess
            && _byteReaderWriter.TryGetSpan(address, sizeof(ushort), out ReadOnlySpan<byte> span, MemoryAccess.Read)
            && span.Length >= sizeof(ushort)) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return (ushort)((_byteReaderWriter[address] << 8) | _byteReaderWriter[address + 1]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, ushort value) {
        if (!Indexable.DisableIndexerSpanAccess
            && _byteReaderWriter.TryGetSpan(address, sizeof(ushort), out Span<byte> span, MemoryAccess.Write)
            && span.Length >= sizeof(ushort)) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            _byteReaderWriter[address] = (byte)(value >>> 8);
            _byteReaderWriter[address + 1] = (byte)value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadValueUnsafe(ref byte source) => BitConverter.IsLittleEndian
        ? BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<ushort>(ref source))
        : Unsafe.ReadUnaligned<ushort>(ref source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValueUnsafe(ref byte destination, ushort value) {
        if (BitConverter.IsLittleEndian) {
            Unsafe.WriteUnaligned(ref destination, BinaryPrimitives.ReverseEndianness(value));
        } else {
            Unsafe.WriteUnaligned(ref destination, value);
        }
    }
}
