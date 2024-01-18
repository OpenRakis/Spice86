namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;

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
}