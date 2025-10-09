namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public class UInt32Indexer : MemoryIndexer<uint> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt32Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <inheritdoc/>
    public override uint this[uint address] {
        get => (uint)(_byteReaderWriter[address] | _byteReaderWriter[address + 1] << 8 |
                      _byteReaderWriter[address + 2] << 16 | _byteReaderWriter[address + 3] << 24);
        set {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >> 8);
            _byteReaderWriter[address + 2] = (byte)(value >> 16);
            _byteReaderWriter[address + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override uint this[ushort segment, ushort offset] {
        get {
            uint address1 = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint address2 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 1)); // Wrap offset within 64KB
            uint address3 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 2)); // Wrap offset within 64KB
            uint address4 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 3)); // Wrap offset within 64KB
            return (uint)(_byteReaderWriter[address1] | _byteReaderWriter[address2] << 8 |
                          _byteReaderWriter[address3] << 16 | _byteReaderWriter[address4] << 24);
        }
        set {
            uint address1 = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint address2 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 1)); // Wrap offset within 64KB
            uint address3 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 2)); // Wrap offset within 64KB
            uint address4 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 3)); // Wrap offset within 64KB
            _byteReaderWriter[address1] = (byte)value;          // Low byte at first address
            _byteReaderWriter[address2] = (byte)(value >> 8);
            _byteReaderWriter[address3] = (byte)(value >> 16);
            _byteReaderWriter[address4] = (byte)(value >> 24);  // High byte at last address
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public override uint this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }
    
    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / 4;
}