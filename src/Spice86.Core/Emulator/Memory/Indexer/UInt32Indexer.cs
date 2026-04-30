namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public class UInt32Indexer : MemoryIndexer<uint> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt32Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, 4) {
        _byteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override uint this[uint address] {
        get => (uint)(_byteReaderWriter[address] | _byteReaderWriter[address + 1] << 8 |
                      _byteReaderWriter[address + 2] << 16 | _byteReaderWriter[address + 3] << 24);
        set {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >> 8);
            _byteReaderWriter[address + 2] = (byte)(value >> 16);
            _byteReaderWriter[address + 3] = (byte)(value >> 24);
        }
    }

    /// <inheritdoc />
    internal override uint ReadSegmented(ushort segment, uint offset) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        uint address3 = Mmu.TranslateAddress(segment, offset + 2);
        uint address4 = Mmu.TranslateAddress(segment, offset + 3);
        return (uint)(_byteReaderWriter[address1] | _byteReaderWriter[address2] << 8 |
                      _byteReaderWriter[address3] << 16 | _byteReaderWriter[address4] << 24);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, uint value) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        uint address3 = Mmu.TranslateAddress(segment, offset + 2);
        uint address4 = Mmu.TranslateAddress(segment, offset + 3);
        _byteReaderWriter[address1] = (byte)value;
        _byteReaderWriter[address2] = (byte)(value >> 8);
        _byteReaderWriter[address3] = (byte)(value >> 16);
        _byteReaderWriter[address4] = (byte)(value >> 24);
    }
    
    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / 4;
}