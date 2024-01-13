namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Implementation of Indexable over a byte array.
/// </summary>
public class ByteArrayBasedIndexable : Indexable {

    /// <summary>
    /// Access to underlying ReaderWriter
    /// </summary>
    public ByteArrayReaderWriter ReaderWriter { get; }

    /// <summary>
    /// Underlying array being wrapped
    /// </summary>
    public byte[] Array { get => ReaderWriter.Array; }

    /// <inheritdoc/>
    public override UInt8Indexer UInt8 { get; }

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 { get; }

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 { get; }

    /// <inheritdoc/>
    public override Int8Indexer Int8 { get; }

    /// <inheritdoc/>
    public override Int16Indexer Int16 { get; }

    /// <inheritdoc/>
    public override Int32Indexer Int32 { get; }

    /// <inheritdoc/>
    public override SegmentedAddressIndexer SegmentedAddress {
        get;
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="array">The byte array used as RAM storage.</param>
    public ByteArrayBasedIndexable(byte[] array) {
        ReaderWriter = new ByteArrayReaderWriter(array);
        (UInt8, UInt16, UInt32, Int8, Int16, Int32, SegmentedAddress) = InstantiateIndexersFromByteReaderWriter(ReaderWriter);
    }
}