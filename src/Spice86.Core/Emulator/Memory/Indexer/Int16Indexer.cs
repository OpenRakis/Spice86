namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Provides indexed signed short access over memory.
/// </summary>
public class Int16Indexer : MemoryIndexer<short> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int16Indexer"/> class with the specified uInt16Indexer.
    /// </summary>
    /// <param name="uInt16Indexer">Where data is read and written.</param>
    public Int16Indexer(UInt16Indexer uInt16Indexer) => _uInt16Indexer = uInt16Indexer;

    /// <inheritdoc/>
    public override short this[uint address] {
        get => (short)_uInt16Indexer[address];
        set => _uInt16Indexer[address] = (ushort)value;
    }
}