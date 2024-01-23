namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Arithmetic Logic Unit code for 8bits operations.
/// </summary>
public class Alu8 : Alu<byte, sbyte, ushort, short> {
    private const byte BeforeMsbMask = 0x40;

    private const byte MsbMask = 0x80;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The class representing the CPU registers, flags, and execution state.</param>
    public Alu8(State state) : base(state) {
    }

    public override byte Add(byte value1, byte value2, bool useCarry) {
        int carry = useCarry && _state.CarryFlag ? 1 : 0;
        byte res = (byte)(value1 + value2 + carry);
        UpdateFlags(res);
        uint carryBits = CarryBitsAdd(value1, value2, res);
        uint overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.CarryFlag = (carryBits >> 7 & 1) == 1;
        _state.AuxiliaryFlag = (carryBits >> 3 & 1) == 1;
        _state.OverflowFlag = (overflowBits >> 7 & 1) == 1;
        return res;
    }

    public override byte And(byte value1, byte value2) {
        byte res = (byte)(value1 & value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public override byte Div(ushort value1, byte value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        uint res = (uint)(value1 / value2);
        if (res > byte.MaxValue) {
            throw new CpuDivisionErrorException($"Division result out of range: {res}");
        }

        return (byte)res;
    }

    public override sbyte Idiv(short value1, sbyte value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        int res = value1 / value2;
        unchecked {
            if (res is > 0x7F or < ((sbyte)0x80)) {
                throw new CpuDivisionErrorException($"Division result out of range: {res}");
            }
        }

        return (sbyte)res;
    }

    public override short Imul(sbyte value1, sbyte value2) {
        int res = value1 * value2;
        bool doesNotFitInByte = res != (sbyte)res;
        _state.OverflowFlag = doesNotFitInByte;
        _state.CarryFlag = doesNotFitInByte;
        return (short)res;
    }


    public override ushort Mul(byte value1, byte value2) {
        ushort res = (ushort)(value1 * value2);
        bool upperHalfNonZero = (res & 0xFF00) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag((byte)res);
        return res;
    }

    public override byte Or(byte value1, byte value2) {
        byte res = (byte)(value1 | value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public override byte Rcl(byte value, byte count) {
        count = (byte)((count & ShiftCountMask) % 9);
        if (count == 0) {
            return value;
        }

        int carry = value >> 8 - count & 0x1;
        byte res = (byte)(value << count);
        int mask = (1 << count - 1) - 1;
        res = (byte)(res | (value >> 9 - count & mask));
        if (_state.CarryFlag) {
            res = (byte)(res | 1 << count - 1);
        }

        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public override byte Rcr(byte value, int count) {
        count = (count & ShiftCountMask) % 9;
        if (count == 0) {
            return value;
        }

        int carry = value >> count - 1 & 0x1;
        int mask = (1 << 8 - count) - 1;
        byte res = (byte)(value >> count & mask);
        res = (byte)(res | value << 9 - count);
        if (_state.CarryFlag) {
            res = (byte)(res | 1 << 8 - count);
        }

        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public override byte Rol(byte value, byte count) {
        count = (byte)((count & ShiftCountMask) % 8);
        if (count == 0) {
            return value;
        }

        int carry = value >> 8 - count & 0x1;
        byte res = (byte)(value << count);
        res = (byte)(res | value >> 8 - count);
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public override byte Ror(byte value, int count) {
        count = (count & ShiftCountMask) % 8;
        if (count == 0) {
            return value;
        }

        int carry = value >> count - 1 & 0x1;
        int mask = (1 << 8 - count) - 1;
        byte res = (byte)(value >> count & mask);
        res = (byte)(res | value << 8 - count);
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public override byte Sar(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        sbyte res = (sbyte)value;
        SetCarryFlagForRightShifts((uint)res, count);
        res >>= count;
        UpdateFlags((byte)res);
        _state.OverflowFlag = false;
        return (byte)res;
    }

    public override byte Shl(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = value << count - 1 & MsbMask;
        _state.CarryFlag = msbBefore != 0;
        byte res = (byte)(value << count);
        UpdateFlags(res);
        int msb = res & MsbMask;
        _state.OverflowFlag = (msb ^ msbBefore) != 0;
        return res;
    }

    public override byte Shld(byte destination, byte source, byte count) {
        throw new NotImplementedException("Shld is not available for 8bits operations");
    }

    public override byte Shr(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask;
        _state.OverflowFlag = msb != 0;
        SetCarryFlagForRightShifts(value, count);
        byte res = (byte)(value >> count);
        UpdateFlags(res);
        return res;
    }

    public override byte Sub(byte value1, byte value2, bool useCarry) {
        int carry = useCarry && _state.CarryFlag ? 1 : 0;
        byte res = (byte)(value1 - value2 - carry);
        UpdateFlags(res);
        uint borrowBits = BorrowBitsSub(value1, value2, res);
        uint overflowBits = OverflowBitsSub(value1, value2, res);
        _state.CarryFlag = (borrowBits >> 7 & 1) == 1;
        _state.AuxiliaryFlag = (borrowBits >> 3 & 1) == 1;
        _state.OverflowFlag = (overflowBits >> 7 & 1) == 1;
        return res;
    }

    public override byte Xor(byte value1, byte value2) {
        byte res = (byte)(value1 ^ value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    private void SetOverflowForRigthRotate8(byte res) {
        bool msb = (res & MsbMask) != 0;
        bool beforeMsb = (res & BeforeMsbMask) != 0;
        _state.OverflowFlag = msb ^ beforeMsb;
    }

    protected override void SetSignFlag(byte value) {
        _state.SignFlag = (value & MsbMask) != 0;
    }
}