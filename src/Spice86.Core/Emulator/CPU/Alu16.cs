namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Arithmetic Logic Unit code for 16bits operations.
/// </summary>
public class Alu16 : Alu<ushort, short, uint, int>  {
    private const ushort BeforeMsbMask = 0x4000;

    private const ushort MsbMask = 0x8000;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU registers and flags</param>
    public Alu16(State state) : base(state) {
    }

    public override ushort Add(ushort value1, ushort value2, bool useCarry) {
        int carry = useCarry && _state.CarryFlag ? 1 : 0;
        ushort res = (ushort)(value1 + value2 + carry);
        UpdateFlags(res);
        uint carryBits = CarryBitsAdd(value1, value2, res);
        uint overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.CarryFlag = (carryBits >> 15 & 1) == 1;
        _state.AuxiliaryFlag = (carryBits >> 3 & 1) == 1;
        _state.OverflowFlag = (overflowBits >> 15 & 1) == 1;
        return res;
    }

    public override ushort And(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 & value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }
    
    /// <inheritdoc/>
    public override ushort Div(uint value1, ushort value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        uint res = value1 / value2;
        if (res > ushort.MaxValue) {
            throw new CpuDivisionErrorException($"Division result out of range: {res}");
        }

        return (ushort)res;
    }

    public override short Idiv(int value1, short value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        int res = value1 / value2;
        unchecked {
            if (res is > 0x7FFF or < (short)0x8000) {
                throw new CpuDivisionErrorException($"Division result out of range: {res}");
            }
        }

        return (short)res;
    }

    public override int Imul(short value1, short value2) {
        int res = value1 * value2;
        bool doesNotFitInWord = res != (short)res;
        _state.OverflowFlag = doesNotFitInWord;
        _state.CarryFlag = doesNotFitInWord;
        return res;
    }

    public override uint Mul(ushort value1, ushort value2) {
        uint res = (uint) (value1 * value2);
        bool upperHalfNonZero = (res & 0xFFFF0000) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag((ushort)res);
        return res;
    }

    public override ushort Or(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 | value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public override ushort Rcl(ushort value, byte count) {
        count = (byte) ((count & ShiftCountMask) % 17);
        if (count == 0) {
            return value;
        }

        int carry = value >> 16 - count & 0x1;
        ushort res = (ushort)(value << count);
        int mask = (1 << count - 1) - 1;
        res = (ushort)(res | (value >> 17 - count & mask));
        if (_state.CarryFlag) {
            res = (ushort)(res | 1 << count - 1);
        }

        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public override ushort Rcr(ushort value, int count) {
        count = (count & ShiftCountMask) % 17;
        if (count == 0) {
            return value;
        }

        int carry = value >> count - 1 & 0x1;
        int mask = (1 << 16 - count) - 1;
        ushort res = (ushort)(value >> count & mask);
        res = (ushort)(res | value << 17 - count);
        if (_state.CarryFlag) {
            res = (ushort)(res | 1 << 16 - count);
        }

        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public override ushort Rol(ushort value, byte count) {
        count = (byte) ((count & ShiftCountMask) % 16);
        if (count == 0) {
            return value;
        }

        int carry = value >> 16 - count & 0x1;
        ushort res = (ushort)(value << count);
        res = (ushort)(res | value >> 16 - count);
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }
    public override ushort Ror(ushort value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        int carry = value >> count - 1 & 0x1;
        int mask = (1 << 16 - count) - 1;
        ushort res = (ushort)(value >> count & mask);
        res = (ushort)(res | value << 16 - count);
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public override ushort Sar(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        short res = (short)value;
        SetCarryFlagForRightShifts((uint)res, count);
        res >>= count;
        UpdateFlags((ushort)res);
        _state.OverflowFlag = false;
        return (ushort)res;
    }

    public override ushort Shl(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = value << count - 1 & MsbMask;
        _state.CarryFlag = msbBefore != 0;
        ushort res = (ushort)(value << count);
        UpdateFlags(res);
        ushort msb = (ushort) (res & MsbMask);
        _state.OverflowFlag = (msb ^ msbBefore) != 0;
        return res;
    }

    public override ushort Shld(ushort destination, ushort source, byte count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return destination;
        }

        if (count > 16) {
            // Undefined. We shift the source in again.
            return (ushort)(source << (count - 16));
        }

        ushort msbBefore = (ushort)(destination & MsbMask);
        _state.CarryFlag = (destination >> (16 - count) & 1) != 0;
        ushort res = (ushort)((destination << count) | (source >> (16 - count)));
        UpdateFlags(res);
        ushort msb = (ushort)(res & MsbMask);
        _state.OverflowFlag = msb != msbBefore;
        return res;
    }

    public override ushort Shr(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        ushort msb = (ushort)(value & MsbMask);
        _state.OverflowFlag = msb != 0;
        SetCarryFlagForRightShifts(value, count);
        ushort res = (ushort)(value >> count);
        UpdateFlags(res);
        return res;
    }

    public override ushort Sub(ushort value1, ushort value2, bool useCarry) {
        int carry = useCarry && _state.CarryFlag ? 1 : 0;
        ushort res = (ushort)(value1 - value2 - carry);
        UpdateFlags(res);
        uint borrowBits = BorrowBitsSub(value1, value2, res);
        uint overflowBits = OverflowBitsSub(value1, value2, res);
        _state.CarryFlag = (borrowBits >> 15 & 1) == 1;
        _state.AuxiliaryFlag = (borrowBits >> 3 & 1) == 1;
        _state.OverflowFlag = (overflowBits >> 15 & 1) == 1;
        return res;
    }

    public override ushort Xor(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 ^ value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    private void SetOverflowForRigthRotate16(ushort res) {
        bool msb = (res & MsbMask) != 0;
        bool beforeMsb = (res & BeforeMsbMask) != 0;
        _state.OverflowFlag = msb ^ beforeMsb;
    }

    protected override void SetSignFlag(ushort value) {
        _state.SignFlag = (value & MsbMask) != 0;
    }

}