namespace Spice86.Core.Emulator.Memory.Indexer;


/// <summary>
/// Implementation of IByteReaderWriter over a byte array
/// </summary>
public class ByteArrayByteReaderWriter : IByteReaderWriter {
    public byte[] Array { get; }

    public byte this[uint address] {
        get => Array[address];
        set => Array[address] = value;
    }

    public uint Length { get => (uint)Array.Length; }

    public ByteArrayByteReaderWriter(byte[] array) {
        Array = array;
    }
}