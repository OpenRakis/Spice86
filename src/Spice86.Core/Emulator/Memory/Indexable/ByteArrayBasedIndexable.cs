namespace Spice86.Core.Emulator.Memory.Indexable;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Implementation of Indexable over a byte array.
/// </summary>
public class ByteArrayBasedIndexable : Indexable {

    // The indexers are kept in fields so the constructor sets no overriding (virtual) property.
    private readonly UInt8Indexer _uInt8;
    private readonly UInt16Indexer _uInt16;
    private readonly UInt32Indexer _uInt32;
    private readonly Int8Indexer _int8;
    private readonly Int16Indexer _int16;
    private readonly UInt16BigEndianIndexer _uInt16BigEndian;
    private readonly Int32Indexer _int32;
    private readonly SegmentedAddress16Indexer _segmentedAddress16;
    private readonly SegmentedAddress32Indexer _segmentedAddress32;

    /// <summary>
    /// Access to underlying ReaderWriter
    /// </summary>
    public ByteArrayReaderWriter ReaderWriter { get; }

    /// <summary>
    /// Underlying array being wrapped
    /// </summary>
    public byte[] Array { get => ReaderWriter.Array; }

    /// <inheritdoc/>
    public override UInt8Indexer UInt8 => _uInt8;

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 => _uInt16;

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 => _uInt32;

    /// <inheritdoc/>
    public override Int8Indexer Int8 => _int8;

    /// <inheritdoc/>
    public override Int16Indexer Int16 => _int16;

    /// <inheritdoc/>
    public override UInt16BigEndianIndexer UInt16BigEndian => _uInt16BigEndian;
    /// <inheritdoc/>
    public override Int32Indexer Int32 => _int32;

    /// <inheritdoc/>
    public override SegmentedAddress16Indexer SegmentedAddress16 => _segmentedAddress16;
    
    /// <inheritdoc/>
    public override SegmentedAddress32Indexer SegmentedAddress32 => _segmentedAddress32;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="array">The byte array used as RAM storage.</param>
    public ByteArrayBasedIndexable(byte[] array) {
        ReaderWriter = new ByteArrayReaderWriter(array);
        (_uInt8, _uInt16, _uInt16BigEndian, _uInt32, _int8, _int16, _int32, _segmentedAddress16, _segmentedAddress32) = InstantiateIndexersFromByteReaderWriter(ReaderWriter, new RealModeMmu8086(), MemoryWriteEvents.Unobserved);
    }
}