namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Provides indexed unsigned 16-byte big endian access over memory.
/// </summary>
public class UInt16BigEndianIndexer : MemoryIndexer<ushort> {
    private readonly IByteReaderWriter _byteReaderWriter;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16BigEndianIndexer"/> class
    /// with the specified <see cref="IByteReaderWriter"/> instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public UInt16BigEndianIndexer(IByteReaderWriter byteReaderWriter) => _byteReaderWriter = byteReaderWriter;

    /// <inheritdoc/>
    public override ushort this[uint address] {
        get => (ushort)(_byteReaderWriter[address + 1] | _byteReaderWriter[address] << 8);
        set {
            _byteReaderWriter[address] = (byte)(value >> 8);
            _byteReaderWriter[address + 1] = (byte)value;
        }
    }
}