namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 16-byte access over memory.
/// </summary>
public class UInt16Indexer {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16Indexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">The object where to read / write bytes.</param>
    public UInt16Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <summary>
    /// Gets or sets the unsigned 16-bit integer at the specified physical address.
    /// </summary>
    /// <param name="address">The physical address of the value to get or set.</param>
    /// <returns>The unsigned 16-bit integer at the specified physical address.</returns>
    public ushort this[uint address] {
        get => (ushort)(_byteReaderWriter[address] | _byteReaderWriter[address + 1] << 8);
        set {
            _byteReaderWriter[address] = (byte)value;
            _byteReaderWriter[address + 1] = (byte)(value >> 8);
        }
    }

    /// <summary>
    /// Gets or sets the unsigned 16-bit integer at the specified segment/offset pair.
    /// </summary>
    /// <param name="segment">The segment of the value to get or set.</param>
    /// <param name="offset">The offset of the value to get or set.</param>
    /// <returns>The unsigned 16-bit integer at the specified segment/offset pair.</returns>
    public ushort this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}