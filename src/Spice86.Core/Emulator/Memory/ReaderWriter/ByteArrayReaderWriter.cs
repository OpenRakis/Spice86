namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Implementation of IReaderWriter over a byte array
/// </summary>
public class ByteArrayReaderWriter : ArrayReaderWriter<byte>, IByteReaderWriter {
    public ByteArrayReaderWriter(byte[] array) : base(array) {
    }
}