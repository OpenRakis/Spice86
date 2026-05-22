namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public sealed class UInt32Indexer : MemoryIndexer<uint> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt32Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, sizeof(uint)) {
        _byteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override uint this[uint address] {
        get => ReadValueCore(address);
        set => WriteValueCore(address, value);
    }

    /// <inheritdoc />
    protected internal override uint ReadSegmented(ushort segment, uint offset) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        uint address3 = Mmu.TranslateAddress(segment, offset + 2);
        uint address4 = Mmu.TranslateAddress(segment, offset + 3);
        if (AreAddressesSequential(address1, address2, address3, address4)) {
            return ReadValueCore(address1);
        } else {
            return _byteReaderWriter[address1]
                 | ((uint)_byteReaderWriter[address2] << 8)
                 | ((uint)_byteReaderWriter[address3] << 16)
                 | ((uint)_byteReaderWriter[address4] << 24);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, uint value) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        uint address3 = Mmu.TranslateAddress(segment, offset + 2);
        uint address4 = Mmu.TranslateAddress(segment, offset + 3);
        if (AreAddressesSequential(address1, address2, address3, address4)) {
            WriteValueCore(address1, value);
        } else {
            _byteReaderWriter[address1] = (byte)value;
            _byteReaderWriter[address2] = (byte)(value >>> 8);
            _byteReaderWriter[address3] = (byte)(value >>> 16);
            _byteReaderWriter[address4] = (byte)(value >>> 24);
        }
    }

    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / sizeof(uint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadValueCore(uint address) {
        if (_byteReaderWriter.TryGetSpan(address, sizeof(uint), out ReadOnlySpan<byte> span, MemoryAccess.Read) &&
                span.Length >= sizeof(uint)) {
            return ReadValueUnsafe(ref MemoryMarshal.GetReference(span));
        } else {
            return _byteReaderWriter[address]
                 | ((uint)_byteReaderWriter[address + 1] << 8)
                 | ((uint)_byteReaderWriter[address + 2] << 16)
                 | ((uint)_byteReaderWriter[address + 3] << 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteValueCore(uint address, uint value) {
        if (_byteReaderWriter.TryGetSpan(address, sizeof(uint), out Span<byte> span, MemoryAccess.Write) &&
                span.Length >= sizeof(uint)) {
            WriteValueUnsafe(ref MemoryMarshal.GetReference(span), value);
        } else {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >>> 8);
            _byteReaderWriter[address + 2] = (byte)(value >>> 16);
            _byteReaderWriter[address + 3] = (byte)(value >>> 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreAddressesSequential(uint address1, uint address2, uint address3, uint address4) {
        // Using bitwise AND instead of logical AND makes this branchless, which is possibly faster.
        return (address2 - address1 == 1)
             & (address3 - address1 == 2)
             & (address4 - address1 == 3);
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
