namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Provides indexed signed byte access over memory.
/// </summary>
public class Int8Indexer : MemoryIndexer<sbyte> {
    private readonly UInt8Indexer _uInt8Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int8Indexer"/> class with the specified uInt8Indexer.
    /// </summary>
    /// <param name="uInt8Indexer">Where data is read and written.</param>
    public Int8Indexer(UInt8Indexer uInt8Indexer, IMmu mmu) : base(mmu, 1) => _uInt8Indexer = uInt8Indexer;

    /// <inheritdoc/>
    public override sbyte this[uint address] {
        get => (sbyte)_uInt8Indexer[address];
        set => _uInt8Indexer[address] = (byte)value;
    }

    /// <inheritdoc />
    internal override sbyte ReadSegmented(ushort segment, uint offset) {
        return (sbyte)_uInt8Indexer.ReadSegmented(segment, offset);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, sbyte value) {
        _uInt8Indexer.WriteSegmented(segment, offset, (byte)value);
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt8Indexer.Count;
}