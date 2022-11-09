namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Utils;

public class GdbFormatter {
    public string FormatValueAsHex32(uint value) {
        return $"{ConvertUtils.Swap32(value):X8}";
    }

    public string FormatValueAsHex8(byte value) {
        return $"{ConvertUtils.Uint8(value):X2}";
    }
}