namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Utils;

/// <summary>
/// Accesses low bytes (least significant byte) of a register based on a given index.
/// </summary>
public class UInt8LowRegistersIndexer : RegistersIndexer<byte> {
    private readonly IUIntReaderWriter _uIntArrayReaderWriter;

    public UInt8LowRegistersIndexer(IUIntReaderWriter uIntArrayReaderWriter) {
        _uIntArrayReaderWriter = uIntArrayReaderWriter;
    }

    public override byte this[uint index] {
        get => ConvertUtils.ReadLsb16(_uIntArrayReaderWriter[index]);
        set {
            uint currentValue = _uIntArrayReaderWriter[index];
            uint newValue = ConvertUtils.WriteLsb(currentValue, value);
            _uIntArrayReaderWriter[index] = newValue;
        }
    }
}