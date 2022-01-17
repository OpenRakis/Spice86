namespace Spice86.Emulator.CPU;

using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;

public class RegistersHolder {

    // 3rd bit in register index means to access the high part
    private const int Register8IndexHighBitMask = 0b100;

    // Registers allowing access to their high / low parts have indexes from 0 to 3 so 2 bits
    private const int Register8IndexHighLowMask = 0b11;

    private readonly int[] _registers;

    private readonly Dictionary<int, string> _registersNames;

    public RegistersHolder(Dictionary<int, string> registersNames) {
        this._registersNames = registersNames;
        this._registers = new int[registersNames.Count];
    }

    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not RegistersHolder other) {
            return false;
        }
        return Enumerable.SequenceEqual(this._registers, other._registers);
    }

    public override int GetHashCode() {
        return HashCode.Combine(this, _registers);
    }

    public String GetReg8Name(int regIndex) {
        string suffix = ((regIndex & Register8IndexHighBitMask) == 1) ? "H" : "L";
        string reg16 = GetRegName(regIndex & Register8IndexHighLowMask);
        return $"{reg16[..1]}{suffix}";
    }

    public int GetRegister(int index) {
        return _registers[index];
    }

    public int GetRegister8H(int regIndex) {
        return ConvertUtils.ReadMsb(GetRegister(regIndex));
    }

    public int GetRegister8L(int regIndex) {
        return ConvertUtils.ReadLsb(GetRegister(regIndex));
    }

    public int GetRegisterFromHighLowIndex8(int index) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            return GetRegister8H(indexInArray);
        }
        return GetRegister8L(indexInArray);
    }

    public String GetRegName(int regIndex) {
        return _registersNames[regIndex];
    }

    public void SetRegister(int index, int value) {
        _registers[index] = ConvertUtils.Uint16(value);
    }

    public void SetRegister8H(int regIndex, int value) {
        int currentValue = GetRegister(regIndex);
        int newValue = ConvertUtils.WriteMsb(currentValue, value);
        SetRegister(regIndex, newValue);
    }

    public void SetRegister8L(int regIndex, int value) {
        int currentValue = GetRegister(regIndex);
        int newValue = ConvertUtils.WriteLsb(currentValue, value);
        SetRegister(regIndex, newValue);
    }

    public void SetRegisterFromHighLowIndex8(int index, int value) {
        int indexInArray = index & Register8IndexHighLowMask;
        if ((index & Register8IndexHighBitMask) != 0) {
            SetRegister8H(indexInArray, value);
        } else {
            SetRegister8L(indexInArray, value);
        }
    }
}