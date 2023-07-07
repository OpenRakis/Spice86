namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public class UInt32Indexer {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt32Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">The object where to read / write bytes.</param>
    public UInt32Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <summary>
    /// Gets or sets the unsigned 32-bit value at the specified memory address.
    /// </summary>
    /// <param name="address">The memory address to access.</param>
    /// <returns>The unsigned 32-bit value at the specified memory address.</returns>
    public uint this[uint address] {
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
    /// Gets or sets the unsigned 32-bit value at the specified segment and offset.
    /// </summary>
    /// <param name="segment">The segment to access.</param>
    /// <param name="offset">The offset within the segment to access.</param>
    /// <returns>The unsigned 32-bit value at the specified segment and offset.</returns>
    public uint this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}