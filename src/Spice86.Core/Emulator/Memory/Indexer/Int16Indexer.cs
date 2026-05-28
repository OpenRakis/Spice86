namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Provides indexed signed short access over memory.
/// </summary>
public sealed class Int16Indexer : MemoryIndexer<short> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int16Indexer"/> class with the specified uInt16Indexer.
    /// </summary>
    /// <param name="uInt16Indexer">Where data is read and written.</param>
    public Int16Indexer(UInt16Indexer uInt16Indexer, IMmu mmu) : base(mmu, sizeof(short)) {
        _uInt16Indexer = uInt16Indexer;
    }

    internal IByteReaderWriter ByteReaderWriter => _uInt16Indexer.ByteReaderWriter;

    /// <inheritdoc/>
    public override short this[uint address] {
        get => (short)_uInt16Indexer[address];
        set => _uInt16Indexer[address] = (ushort)value;
    }

    /// <inheritdoc />
    protected internal override short ReadSegmented(ushort segment, uint offset) {
        return (short)_uInt16Indexer.ReadSegmented(segment, offset);
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, short value) {
        _uInt16Indexer.WriteSegmented(segment, offset, (ushort)value);
    }

    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count;
}
