namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Provides indexed signed int access over memory.
/// </summary>
public class Int32Indexer : MemoryIndexer<int> {
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int32Indexer"/> class with the specified uInt32Indexer.
    /// </summary>
    /// <param name="uInt32Indexer">Where data is read and written.</param>
    public Int32Indexer(UInt32Indexer uInt32Indexer, IMmu mmu) : base(mmu, 4) => _uInt32Indexer = uInt32Indexer;

    /// <inheritdoc/>
    public override int this[uint address] {
        get => (int)_uInt32Indexer[address];
        set => _uInt32Indexer[address] = (uint)value;
    }

    /// <inheritdoc />
    internal override int ReadSegmented(ushort segment, uint offset) {
        return (int)_uInt32Indexer.ReadSegmented(segment, offset);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, int value) {
        _uInt32Indexer.WriteSegmented(segment, offset, (uint)value);
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt32Indexer.Count;
}