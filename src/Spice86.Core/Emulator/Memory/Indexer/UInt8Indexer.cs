namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed byte access over memory.
/// </summary>
public class UInt8Indexer {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">The object where to read / write bytes.</param>
    public UInt8Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <summary>
    /// Gets or sets the 8-bit unsigned integer at the specified index in the memory.
    /// </summary>
    /// <param name="i">The index of the element to get or set.</param>
    /// <returns>The 8-bit unsigned integer at the specified index in the memory.</returns>
    public byte this[uint i] {
        get => _byteReaderWriter[i];
        set => _byteReaderWriter[i] = value;
    }

    /// <summary>
    /// Gets or sets the 8-bit unsigned integer at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    /// <returns>The 8-bit unsigned integer at the specified segment and offset in the memory.</returns>
    public byte this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}