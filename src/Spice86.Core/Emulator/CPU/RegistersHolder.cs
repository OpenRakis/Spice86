namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Utils;

using System;
using System.Collections.Generic;
public class RegistersHolder {

    // 3rd bit in register index means to access the high part
    private const int Register8IndexHighBitMask = 0b100;

    // Registers allowing access to their high / low parts have indexes from 0 to 3 so 2 bits
    private const int Register8IndexHighLowMask = 0b11;

    private readonly ushort[] _registers;

    private readonly Dictionary<int, string> _registersNames;

    protected RegistersHolder(Dictionary<int, string> registersNames) {
        _registersNames = registersNames;
        _registers = new ushort[registersNames.Count];
    }

    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not RegistersHolder other) {
            return false;
        }
        return _registers.AsSpan().SequenceEqual(other._registers);
    }

    public override int GetHashCode() {
        return HashCode.Combine(this, _registers);
    }

    public string GetReg8Name(int regIndex) {
        string suffix = (regIndex & Register8IndexHighBitMask) == 1 ? "H" : "L";
        string reg16 = GetRegName(regIndex & Register8IndexHighLowMask);
        return $"{reg16[..1]}{suffix}";
    }

    public ushort GetRegister(int index) {
        return _registers[index];
    }

    public byte GetRegister8H(int regIndex) {
        return ConvertUtils.ReadMsb(GetRegister(regIndex));
    }

    public byte GetRegister8L(int regIndex) {
        return ConvertUtils.ReadLsb(GetRegister(regIndex));
    }

    public byte GetRegisterFromHighLowIndex8(int index) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            return GetRegister8H(indexInArray);
        }
        return GetRegister8L(indexInArray);
    }

    public string GetRegName(int regIndex) {
        return _registersNames[regIndex];
    }

    public void SetRegister(int index, ushort value) {
        _registers[index] = value;
    }

    public void SetRegister8H(int regIndex, byte value) {
        ushort currentValue = GetRegister(regIndex);
        ushort newValue = ConvertUtils.WriteMsb(currentValue, value);
        SetRegister(regIndex, newValue);
    }

    public void SetRegister8L(int regIndex, byte value) {
        ushort currentValue = GetRegister(regIndex);
        ushort newValue = ConvertUtils.WriteLsb(currentValue, value);
        SetRegister(regIndex, newValue);
    }

    public void SetRegisterFromHighLowIndex8(int index, byte value) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            SetRegister8H(indexInArray, value);
        } else {
            SetRegister8L(indexInArray, value);
        }
    }
}