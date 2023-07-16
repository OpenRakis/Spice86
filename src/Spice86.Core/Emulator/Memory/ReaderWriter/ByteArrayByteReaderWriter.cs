namespace Spice86.Core.Emulator.Memory.ReaderWriter;


/// <summary>
/// Implementation of IByteReaderWriter over a byte array
/// </summary>
public class ByteArrayByteReaderWriter : IByteReaderWriter {
    public byte[] Array { get; }

    /// <inheritdoc/>
    public byte this[uint address] {
        get => Array[address];
        set => Array[address] = value;
    }

    /// <inheritdoc/>
    public uint Length { get => (uint)Array.Length; }

    public ByteArrayByteReaderWriter(byte[] array) {
        Array = array;
    }
}