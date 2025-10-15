namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public class UInt32Indexer : MemoryIndexer<uint> {
    private readonly UInt8Indexer _uint8Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="uint8Indexer">Where data is read and written.</param>
    public UInt32Indexer(UInt8Indexer uint8Indexer) => _uint8Indexer = uint8Indexer;

    /// <inheritdoc/>
    public override uint this[uint address] {
        get => (uint)(_uint8Indexer[address] | _uint8Indexer[address + 1] << 8 |
                      _uint8Indexer[address + 2] << 16 | _uint8Indexer[address + 3] << 24);
        set {
            _uint8Indexer[address] = (byte)value;
            _uint8Indexer[address + 1] = (byte)(value >> 8);
            _uint8Indexer[address + 2] = (byte)(value >> 16);
            _uint8Indexer[address + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override uint this[ushort segment, ushort offset] {
        get =>
            (uint)(_uint8Indexer[segment, offset] |
                   _uint8Indexer[segment, (ushort)(offset + 1)] << 8 |
                   _uint8Indexer[segment, (ushort)(offset + 2)] << 16 |
                   _uint8Indexer[segment, (ushort)(offset + 3)] << 24);
        set {
            _uint8Indexer[segment, offset] = (byte)value;          // Low byte at first address
            _uint8Indexer[segment, (ushort)(offset + 1)] = (byte)(value >> 8);
            _uint8Indexer[segment, (ushort)(offset + 2)] = (byte)(value >> 16);
            _uint8Indexer[segment, (ushort)(offset + 3)] = (byte)(value >> 24);  // High byte at last address
        }
    }
    
    /// <inheritdoc/>
    public override int Count => _uint8Indexer.Count / 4;
}