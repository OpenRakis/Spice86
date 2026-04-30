namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed unsigned 16-byte access over memory.
/// </summary>
public class UInt16Indexer : MemoryIndexer<ushort> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16Indexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt16Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, 2) {
        _byteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override ushort this[uint address] {
        get => (ushort)(_byteReaderWriter[address] | _byteReaderWriter[address + 1] << 8);
        set {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >> 8);
        }
    }

    /// <inheritdoc />
    internal override ushort ReadSegmented(ushort segment, uint offset) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        return (ushort)(_byteReaderWriter[address1] | _byteReaderWriter[address2] << 8);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, ushort value) {
        uint address1 = Mmu.TranslateAddress(segment, offset);
        uint address2 = Mmu.TranslateAddress(segment, offset + 1);
        _byteReaderWriter[address1] = (byte)value;
        _byteReaderWriter[address2] = (byte)(value >> 8);
    }
    
    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / 2;
}