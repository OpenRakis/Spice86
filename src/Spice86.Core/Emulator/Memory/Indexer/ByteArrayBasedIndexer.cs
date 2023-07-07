namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Implementation of IIndexed over a byte array.
/// </summary>
public class ByteArrayBasedIndexer : IIndexed {
    private readonly ByteArrayByteReaderWriter _readerWriter;

    public byte[] Array { get => _readerWriter.Array; }

    /// <inheritdoc/>
    public UInt8Indexer UInt8 { get; }

    /// <inheritdoc/>
    public UInt16Indexer UInt16 { get; }

    /// <inheritdoc/>
    public UInt32Indexer UInt32 { get; }

    /// <inheritdoc/>
    public OffsetSegmentIndexer OffsetSegment { get; }

    public ByteArrayBasedIndexer(byte[] array) {
        _readerWriter = new ByteArrayByteReaderWriter(array);
        UInt8 = new UInt8Indexer(_readerWriter);
        UInt16 = new UInt16Indexer(_readerWriter);
        UInt32 = new UInt32Indexer(_readerWriter);
        OffsetSegment = new OffsetSegmentIndexer(UInt16);
    }
}