namespace Spice86.Core.Emulator.Gdb;

using Spice86.Shared.Utils;

/// <summary>
/// Utility class for formatting values in a way compatible with GDB.
/// </summary>
public class GdbFormatter {

    /// <summary>
    /// Formats the given 32-bit value as a hexadecimal string with a length of 8 characters.
    /// </summary>
    /// <param name="value">The 32-bit value to format.</param>
    /// <returns>The formatted string.</returns>
    public string FormatValueAsHex32(uint value) {
        return $"{ConvertUtils.Swap32(value):X8}";
    }

    /// <summary>
    /// Formats the given 8-bit value as a hexadecimal string with a length of 2 characters.
    /// </summary>
    /// <param name="value">The 8-bit value to format.</param>
    /// <returns>The formatted string.</returns>
    public string FormatValueAsHex8(byte value) {
        return $"{value:X2}";
    }
}