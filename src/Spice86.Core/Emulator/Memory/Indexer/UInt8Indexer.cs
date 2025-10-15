namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed byte access over memory.
/// </summary>
public class UInt8Indexer : MemoryIndexer<byte> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Indexer"/> class with the specified byteReadeWriter.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt8Indexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <inheritdoc/>
    public override byte this[uint address] {
        get => _byteReaderWriter[address];
        set => _byteReaderWriter[address] = value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override byte this[ushort segment, ushort offset] {
        // Access is done via the absolute reader writer. Segmented addresses are never relative.
        get => _byteReaderWriter.AbsoluteReaderWriter[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => _byteReaderWriter.AbsoluteReaderWriter[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }

    /// <inheritdoc/>
    public override int Count => _byteReaderWriter.Length;
}