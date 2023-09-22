namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Accesses 16-bit registers based on a given index.
/// </summary>
public class UInt16RegistersIndexer : RegistersIndexer<ushort> {
    private readonly IUIntReaderWriter _uIntArrayReaderWriter;

    public UInt16RegistersIndexer(IUIntReaderWriter uIntArrayReaderWriter) {
        _uIntArrayReaderWriter = uIntArrayReaderWriter;
    }

    public override ushort this[uint index] {
        get => (ushort)_uIntArrayReaderWriter[index];
        set {
            uint currentValue = _uIntArrayReaderWriter[index];
            uint newValue = (currentValue & 0xFFFF0000) | value;
            _uIntArrayReaderWriter[index] = newValue;
        }
    }
}