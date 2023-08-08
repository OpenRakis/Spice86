namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Provides indexed signed byte access over memory.
/// </summary>
public class Int8Indexer : Indexer<sbyte> {
    private readonly UInt8Indexer _uInt8Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int8Indexer"/> class with the specified uInt8Indexer.
    /// </summary>
    /// <param name="uInt8Indexer">Where data is read and written.</param>
    public Int8Indexer(UInt8Indexer uInt8Indexer) => _uInt8Indexer = uInt8Indexer;

    /// <inheritdoc/>
    public override sbyte this[uint address] {
        get => (sbyte)_uInt8Indexer[address];
        set => _uInt8Indexer[address] = (byte)value;
    }
}