namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Provides indexed signed byte access over memory.
/// </summary>
public sealed class Int8Indexer : MemoryIndexer<sbyte> {
    private readonly UInt8Indexer _uInt8Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int8Indexer"/> class with the specified uInt8Indexer.
    /// </summary>
    /// <param name="uInt8Indexer">Where data is read and written.</param>
    public Int8Indexer(UInt8Indexer uInt8Indexer, IMmu mmu) : base(mmu, sizeof(sbyte)) {
        _uInt8Indexer = uInt8Indexer;
    }

    internal IByteReaderWriter ByteReaderWriter => _uInt8Indexer.ByteReaderWriter;

    /// <inheritdoc/>
    public override sbyte this[uint address] {
        get => (sbyte)_uInt8Indexer[address];
        set => _uInt8Indexer[address] = (byte)value;
    }

    /// <inheritdoc />
    protected internal override sbyte ReadSegmented(ushort segment, uint offset) {
        return (sbyte)_uInt8Indexer.ReadSegmented(segment, offset);
    }

    /// <inheritdoc />
    protected internal override void WriteSegmented(ushort segment, uint offset, sbyte value) {
        _uInt8Indexer.WriteSegmented(segment, offset, (byte)value);
    }

    /// <inheritdoc/>
    public override int Count => _uInt8Indexer.Count;
}
