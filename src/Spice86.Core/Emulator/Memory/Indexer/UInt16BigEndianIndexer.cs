namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Provides indexed unsigned 16-byte big endian access over memory.
/// </summary>
public class UInt16BigEndianIndexer : MemoryIndexer<ushort> {
    private readonly UInt8Indexer _uint8Indexer;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16BigEndianIndexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="uint8Indexer">Where data is read and written.</param>
    public UInt16BigEndianIndexer(UInt8Indexer uint8Indexer) => _uint8Indexer = uint8Indexer;

    /// <inheritdoc/>
    public override ushort this[uint address] {
        get => (ushort)(_uint8Indexer[address + 1] | _uint8Indexer[address] << 8);
        set {
            _uint8Indexer[address] = (byte)(value >> 8);
            _uint8Indexer[address + 1] = (byte)value;
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override ushort this[ushort segment, ushort offset] {
        get => (ushort)(_uint8Indexer[segment, (ushort)(offset + 1)] | _uint8Indexer[segment, offset] << 8);
        set {
            _uint8Indexer[segment, offset] = (byte)(value >> 8);
            _uint8Indexer[segment, (ushort)(offset + 1)] = (byte)value;
        }
    }
    
    /// <inheritdoc/>
    public override int Count => _uint8Indexer.Count / 2;
}