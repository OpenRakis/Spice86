namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Provides indexed signed int access over memory.
/// </summary>
public class Int32Indexer : Indexer<int> {
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int32Indexer"/> class with the specified uInt32Indexer.
    /// </summary>
    /// <param name="uInt32Indexer">Where data is read and written.</param>
    public Int32Indexer(UInt32Indexer uInt32Indexer) => _uInt32Indexer = uInt32Indexer;

    /// <inheritdoc/>
    public override int this[uint address] {
        get => (int)_uInt32Indexer[address];
        set => _uInt32Indexer[address] = (uint)value;
    }
}