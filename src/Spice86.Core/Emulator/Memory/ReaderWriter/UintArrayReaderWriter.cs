namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Implementation of IReaderWriter over a uint array
/// </summary>
public class UIntArrayReaderWriter : ArrayReaderWriter<uint>, IUIntReaderWriter {
    public UIntArrayReaderWriter(uint[] array) : base(array) {
    }
}