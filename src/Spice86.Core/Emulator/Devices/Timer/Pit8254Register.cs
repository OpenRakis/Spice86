namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Shared.Utils;

/// <summary>
/// Access algorithm for PIT8254 registers.
/// Registers are 16 bits but are accessed via 8 bit read.
/// </summary>
public class Pit8254Register {
    private ushort _partiallyWrittenValue;

    public ushort Value { get; set; }

    public bool ValueFullyRead { get; private set; } = true;

    public bool ValueFullyWritten { get; private set; } = true;

    /// <summary>
    /// Read the 16bit value byte by byte.
    /// Property ValueTotallyRead can be used to determine if value has been totally read or not
    /// </summary>
    /// <param name="readWritePolicy">How to read the value</param>
    /// <returns>The byte read</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public byte ReadValue(int readWritePolicy) {
        switch (readWritePolicy) {
            case 1:
                ValueFullyRead = true;
                return ConvertUtils.ReadLsb(Value);
            case 2:
                ValueFullyRead = true;
                return ConvertUtils.ReadMsb(Value);
            case 3:
                // LSB first, then MSB
                if (ValueFullyRead) {
                    ValueFullyRead = false;
                    return ConvertUtils.ReadLsb(Value);
                }
                ValueFullyRead = true;
                return ConvertUtils.ReadMsb(Value);
            default: throw new ArgumentOutOfRangeException($"Invalid readWritePolicy {readWritePolicy}");
        }
    }

    /// <summary>
    /// Writes to the given 16 bit value byte by byte.
    /// Property ValueTotallyWritten can be used to determine if value has been totally written or not
    /// </summary>
    /// <param name="readWritePolicy">How to write the value</param>
    /// <param name="targetValue"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void WriteValue(int readWritePolicy, byte targetValue) {
        switch (readWritePolicy) {
            case 1:
                ValueFullyWritten = true;
                Value = ConvertUtils.WriteLsb(Value, targetValue);
                break;
            case 2:
                ValueFullyWritten = true;
                Value = ConvertUtils.WriteMsb16(Value, targetValue);
                break;
            case 3:
                // LSB first, then MSB
                if (ValueFullyWritten) {
                    ValueFullyWritten = false;
                    _partiallyWrittenValue = Value;
                    _partiallyWrittenValue = ConvertUtils.WriteLsb(_partiallyWrittenValue, targetValue);
                    break;
                }
                ValueFullyWritten = true;
                Value = ConvertUtils.WriteMsb16(_partiallyWrittenValue, targetValue);
                break;
            default: throw new ArgumentOutOfRangeException($"Invalid readWritePolicy {readWritePolicy}");
        }
    }
}