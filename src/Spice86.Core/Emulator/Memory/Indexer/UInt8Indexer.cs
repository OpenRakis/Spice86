namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Provides indexed byte access over memory.
/// </summary>
public sealed class UInt8Indexer : MemoryIndexer<byte> {
    internal IByteReaderWriter ByteReaderWriter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt8Indexer(IByteReaderWriter byteReaderWriter, IMmu mmu) : base(mmu, sizeof(byte)) {
        ByteReaderWriter = byteReaderWriter;
    }

    /// <inheritdoc/>
    public override byte this[uint address] {
        get => ByteReaderWriter[address];
        set => ByteReaderWriter[address] = value;
    }

    /// <inheritdoc />
    protected internal override byte ReadSegmented(ushort segment, uint offset) {
        return ByteReaderWriter[Mmu.TranslateAddress(segment, offset)];
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, byte value) {
        ByteReaderWriter[Mmu.TranslateAddress(segment, offset)] = value;
    }

    /// <inheritdoc/>
    public override int Count => ByteReaderWriter.Length;
}
