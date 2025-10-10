namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 16-byte access over memory.
/// </summary>
public class UInt16Indexer : MemoryIndexer<ushort> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16Indexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt16Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <inheritdoc/>
    public override ushort this[uint address] {
        get => (ushort)(_byteReaderWriter[address] | _byteReaderWriter[address + 1] << 8);
        set {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >> 8);
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override ushort this[ushort segment, ushort offset] {
        get {
            uint address1 = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint address2 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 1)); // Wrap offset within 64KB
            return (ushort)(_byteReaderWriter[address1] | _byteReaderWriter[address2] << 8);
        }
        set {
            uint address1 = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint address2 = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 1));
            _byteReaderWriter[address1] = (byte)value;          // Low byte at first address
            _byteReaderWriter[address2] = (byte)(value >> 8);   // High byte at second address
        }
    }
    
    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length / 2;
}