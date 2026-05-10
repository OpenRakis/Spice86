namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed byte access over memory.
/// </summary>
public class UInt8Indexer : MemoryIndexer<byte> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt8Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, 1) {
        _byteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override byte this[uint address] {
        get => _byteReaderWriter[address];
        set => _byteReaderWriter[address] = value;
    }

    /// <inheritdoc />
    internal override byte ReadSegmented(ushort segment, uint offset) {
        return _byteReaderWriter[Mmu.TranslateAddress(segment, offset)];
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, byte value) {
        _byteReaderWriter[Mmu.TranslateAddress(segment, offset)] = value;
    }

    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length;
}