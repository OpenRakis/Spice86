namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;

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
}