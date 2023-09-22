namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Accesses 32-bit registers based on a given index.
/// </summary>
public class UInt32RegistersIndexer : RegistersIndexer<uint> {
    private readonly IUIntReaderWriter _uIntArrayReaderWriter;

    public UInt32RegistersIndexer(IUIntReaderWriter uIntArrayReaderWriter) {
        _uIntArrayReaderWriter = uIntArrayReaderWriter;
    }

    public override uint this[uint index] {
        get => _uIntArrayReaderWriter[index];
        set => _uIntArrayReaderWriter[index] = value;
    }
}