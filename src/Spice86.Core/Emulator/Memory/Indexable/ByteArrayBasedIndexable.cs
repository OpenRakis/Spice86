namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Implementation of Indexable over a byte array.
/// </summary>
public class ByteArrayBasedIndexable : Indexable {
    private readonly ByteArrayByteReaderWriter _readerWriter;

    /// <summary>
    /// Underlying array beeing wrapped
    /// </summary>
    public byte[] Array { get => _readerWriter.Array; }

    /// <inheritdoc/>
    public override UInt8Indexer UInt8 { get; }

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 { get; }

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 { get; }

    /// <inheritdoc/>
    public override SegmentedAddressValueIndexer SegmentedAddressValue { get; }

    /// <inheritdoc/>
    public override SegmentedAddressIndexer SegmentedAddress {
        get;
    }

    public ByteArrayBasedIndexable(byte[] array) {
        _readerWriter = new ByteArrayByteReaderWriter(array);
        (UInt8, UInt16, UInt32, SegmentedAddressValue, SegmentedAddress) = InstantiateIndexersFromByteReaderWriter(_readerWriter);
    }
}