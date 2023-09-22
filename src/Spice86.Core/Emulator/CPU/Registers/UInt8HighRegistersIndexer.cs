namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Utils;

/// <summary>
/// Accesses high bytes (most significant byte) of a register based on a given index.
/// </summary>
public class UInt8HighRegistersIndexer : RegistersIndexer<byte> {
    private readonly IUIntReaderWriter _uIntArrayReaderWriter;

    public UInt8HighRegistersIndexer(IUIntReaderWriter uIntArrayReaderWriter) {
        _uIntArrayReaderWriter = uIntArrayReaderWriter;
    }

    public override byte this[uint index] {
        get => ConvertUtils.ReadMsb16(_uIntArrayReaderWriter[index]);
        set {
            uint currentValue =_uIntArrayReaderWriter[index];
            uint newValue = ConvertUtils.WriteMsb16(currentValue, value);
            _uIntArrayReaderWriter[index] = newValue;
        }
    }
}