namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// A base class that represents a set of CPU registers.
/// </summary>
public class RegistersHolder {
    private readonly uint[] _registers;

    private readonly Dictionary<uint, string> _registersNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersHolder"/> class with the specified register names.
    /// </summary>
    /// <param name="registersNames">The names of the registers.</param>
    protected RegistersHolder(Dictionary<uint, string> registersNames) {
        _registersNames = registersNames;
        _registers = new uint[registersNames.Count];
        IUIntReaderWriter readerWriter = new UIntArrayReaderWriter(_registers);
        UInt32 = new(readerWriter);
        UInt16 = new(readerWriter);
        UInt8HighRegistersIndexer high = new(readerWriter);
        UInt8LowRegistersIndexer low = new(readerWriter);
        UInt8Low = low;
        UInt8High = high;
        UInt8HighLow = new(high, low);
    }

    public UInt8LowRegistersIndexer UInt8Low { get; }
    public UInt8HighRegistersIndexer UInt8High { get; }
    public UInt8HighLowRegistersIndexer UInt8HighLow { get; }
    public UInt16RegistersIndexer UInt16 { get; }
    public UInt32RegistersIndexer UInt32 { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not RegistersHolder other) {
            return false;
        }
        return _registers.AsSpan().SequenceEqual(other._registers);
    }

    /// <summary>
    /// Compute the hash code of the class instance.
    /// </summary>
    /// <returns>The combined hash of the instance, and the <see cref="_registers"/> array.</returns>
    public override int GetHashCode() {
        return _registers.GetHashCode();
    }

    /// <summary>
    /// Gets the name of the 8-bit register with the specified index.
    /// </summary>
    /// <param name="regIndex">The index of the register.</param>
    /// <returns>The name of the register.</returns>
    public string GetReg8Name(uint regIndex) {
        string suffix = UInt8HighLow.IsHigh(regIndex) ? "H" : "L";
        string reg16 = GetRegName(UInt8HighLow.ComputeRegisterIndexInArray(regIndex));
        return $"{reg16[..1]}{suffix}";
    }

    /// <summary>
    /// Gets the name of a register based on a given index.
    /// </summary>
    /// <param name="regIndex">The index of the register to get the name of.</param>
    /// <returns>The name of the register.</returns>
    public string GetRegName(uint regIndex) {
        return _registersNames[regIndex];
    }
}
