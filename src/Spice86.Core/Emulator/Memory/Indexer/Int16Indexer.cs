namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Provides indexed signed short access over memory.
/// </summary>
public class Int16Indexer : MemoryIndexer<short> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int16Indexer"/> class with the specified uInt16Indexer.
    /// </summary>
    /// <param name="uInt16Indexer">Where data is read and written.</param>
    public Int16Indexer(UInt16Indexer uInt16Indexer, IMmu mmu) : base(mmu, 2) => _uInt16Indexer = uInt16Indexer;

    /// <inheritdoc/>
    public override short this[uint address] {
        get => (short)_uInt16Indexer[address];
        set => _uInt16Indexer[address] = (ushort)value;
    }

    /// <inheritdoc />
    internal override short ReadSegmented(ushort segment, uint offset) {
        return (short)_uInt16Indexer.ReadSegmented(segment, offset);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, short value) {
        _uInt16Indexer.WriteSegmented(segment, offset, (ushort)value);
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count;
}