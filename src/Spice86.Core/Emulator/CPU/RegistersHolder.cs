namespace Spice86.Core.Emulator.CPU;

using Spice86.Shared.Utils;

using System.Collections.Frozen;

/// <summary>
/// A base class that represents a set of CPU registers.
/// </summary>
public class RegistersHolder {
    /// <summary>
    /// 3rd bit in register index means to access the high part
    /// </summary>
    private const int Register8IndexHighBitMask = 0b100;

    /// <summary>
    /// Registers allowing access to their high / low parts have indexes from 0 to 3 so 2 bits
    /// </summary>
    private const int Register8IndexHighLowMask = 0b11;

    private readonly uint[] _registers;

    private readonly FrozenDictionary<int, string> _registersNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersHolder"/> class with the specified register names.
    /// </summary>
    /// <param name="registersNames">The names of the registers.</param>
    protected RegistersHolder(FrozenDictionary<int, string> registersNames) {
        _registersNames = registersNames;
        _registers = new uint[registersNames.Count];
    }

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
    public string GetReg8Name(int regIndex) {
        string suffix = (regIndex & Register8IndexHighBitMask) == 1 ? "H" : "L";
        string reg16 = GetRegName(regIndex & Register8IndexHighLowMask);
        return $"{reg16[..1]}{suffix}";
    }

    /// <summary>
    /// Gets the 32-bit register with the specified index.
    /// </summary>
    /// <param name="index">The index of the register.</param>
    /// <returns>The value of the register.</returns>
    public uint GetRegister32(int index) {
        return _registers[index];
    }

    /// <summary>
    /// Gets the 16-bit register with the specified index.
    /// </summary>
    /// <param name="index">The index of the register.</param>
    /// <returns>The value of the register.</returns>
    public ushort GetRegister16(int index) {
        return (ushort)(GetRegister32(index) & 0xFFFF);
    }

    /// <summary>
    /// Gets the high 8 bits of the 16-bit register with the specified index.
    /// </summary>
    /// <param name="regIndex">The index of the register.</param>
    /// <returns>The value of the high 8 bits of the register.</returns>
    public byte GetRegister8H(int regIndex) {
        return ConvertUtils.ReadMsb(GetRegister16(regIndex));
    }

    /// <summary>
    /// Gets the low 8 bits of the 16-bit register with the specified index.
    /// </summary>
    /// <param name="regIndex">The index of the register.</param>
    /// <returns>The value of the low 8 bits of the register.</returns>
    public byte GetRegister8L(int regIndex) {
        return ConvertUtils.ReadLsb(GetRegister16(regIndex));
    }

    /// <summary>
    /// Gets a byte from a register based on a given index that represents a high/low byte.
    /// </summary>
    /// <param name="index">The index of the register to get a byte from.</param>
    /// <returns>The byte value of the register.</returns>
    public byte GetRegisterFromHighLowIndex8(int index) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            return GetRegister8H(indexInArray);
        }
        return GetRegister8L(indexInArray);
    }

    /// <summary>
    /// Gets the name of a register based on a given index.
    /// </summary>
    /// <param name="regIndex">The index of the register to get the name of.</param>
    /// <returns>The name of the register.</returns>
    public string GetRegName(int regIndex) {
        return _registersNames[regIndex];
    }

    /// <summary>
    /// Sets a 32-bit value to a register based on a given index.
    /// </summary>
    /// <param name="index">The index of the register to set the value of.</param>
    /// <param name="value">The value to set the register to.</param>
    public void SetRegister32(int index, uint value) {
        _registers[index] = value;
    }

    /// <summary>
    /// Sets a 16-bit value to a register based on a given index.
    /// </summary>
    /// <param name="index">The index of the register to set the value of.</param>
    /// <param name="value">The value to set the register to.</param>
    public void SetRegister16(int index, ushort value) {
        uint currentValue = GetRegister32(index);
        uint newValue = (currentValue & 0xFFFF0000) | value;
        SetRegister32(index, newValue);
    }

    /// <summary>
    /// Sets a high byte (most significant byte) of a register based on a given index.
    /// </summary>
    /// <param name="regIndex">The index of the register to set the high byte of.</param>
    /// <param name="value">The value to set the high byte to.</param>
    public void SetRegister8H(int regIndex, byte value) {
        ushort currentValue = GetRegister16(regIndex);
        ushort newValue = ConvertUtils.WriteMsb(currentValue, value);
        SetRegister16(regIndex, newValue);
    }

    /// <summary>
    /// Sets a low byte (least significant byte) of a register based on a given index.
    /// </summary>
    /// <param name="regIndex">The index of the register to set the low byte of.</param>
    /// <param name="value">The value to set the low byte to.</param>
    public void SetRegister8L(int regIndex, byte value) {
        ushort currentValue = GetRegister16(regIndex);
        ushort newValue = ConvertUtils.WriteLsb(currentValue, value);
        SetRegister16(regIndex, newValue);
    }

    /// <summary>
    /// Sets a byte value to a register based on a given index that represents a high/low byte.
    /// </summary>
    /// <param name="index">The index of the register to set a byte to.</param>
    /// <param name="value">The value to set the register to.</param>
    public void SetRegisterFromHighLowIndex8(int index, byte value) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            SetRegister8H(indexInArray, value);
        } else {
            SetRegister8L(indexInArray, value);
        }
    }
}
