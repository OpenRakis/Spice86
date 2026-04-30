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
        // Undocumented: real CPUs clear AF for logical operations
        _state.AuxiliaryFlag = false;
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
        UpdateFlags((byte)res);
        _state.AuxiliaryFlag = false;
        return (short)res;
    }


    public override ushort Mul(byte value1, byte value2) {
        ushort res = (ushort)(value1 * value2);
        bool upperHalfNonZero = (res & 0xFF00) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        _state.ZeroFlag = (res & 0x00FF) == 0;
        return res;
    }

    public override byte Or(byte value1, byte value2) {
        byte res = (byte)(value1 | value2);
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        // Undocumented 8086
        _state.AuxiliaryFlag = false;
        return res;
    }

    public override byte Rcl(byte value, byte count) {
        int maskedCount = count & ShiftCountMask;
        if (maskedCount == 0) {
            return value;
        }

        count = (byte)(maskedCount % 9);
        if (count == 0) {
            _state.OverflowFlag = _state.CarryFlag ^ ((value & MsbMask) != 0);
            return value;
        }

        bool oldCarry = _state.CarryFlag;
        int carry = value >> 8 - count & 0x1;
        byte res = (byte)(value << count);
        int mask = (1 << count - 1) - 1;
        res = (byte)(res | (value >> 9 - count & mask));
        if (oldCarry) {
            res = (byte)(res | 1 << count - 1);
        }

        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = _state.CarryFlag ^ msb;
        return res;
    }

    public override byte Rcr(byte value, int count) {
        int maskedCount = count & ShiftCountMask;
        if (maskedCount == 0) {
            return value;
        }

        count = maskedCount % 9;
        if (count == 0) {
            SetOverflowForRigthRotate8(value);
            return value;
        }

        bool oldCarry = _state.CarryFlag;
        int carry = value >> count - 1 & 0x1;
        int mask = (1 << 8 - count) - 1;
        byte res = (byte)(value >> count & mask);
        res = (byte)(res | value << 9 - count);
        if (oldCarry) {
            res = (byte)(res | 1 << 8 - count);
        }

        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public override byte Rol(byte value, byte count) {
        int maskedCount = count & ShiftCountMask;
        if (maskedCount == 0) {
            return value;
        }
        int effective = maskedCount % 8;
        byte res = effective == 0
            ? value
            : (byte)((value << effective) | (value >> 8 - effective));
        int carry = res & 0x1;
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        bool lsb = (res & 0x01) != 0;
        _state.OverflowFlag = msb ^ lsb;
        return res;
    }

    public override byte Ror(byte value, int count) {
        int maskedCount = count & ShiftCountMask;
        if (maskedCount == 0) {
            return value;
        }
        int effective = maskedCount % 8;
        byte res = effective == 0
            ? value
            : (byte)((value >> effective) | (value << 8 - effective));
        int carry = (res & MsbMask) != 0 ? 1 : 0;
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

        byte res;
        bool carry;
        if (count <= 8) {
            int msbBefore = value << count - 1 & MsbMask;
            carry = msbBefore != 0;
            res = (byte)(value << count);
        } else {
            // Real 80386 byte SHL behavior for masked counts > 8: result is 0,
            // CF cycles back to bit 0 of the original at multiples of 8 (count
            // 16, 24); for non-multiples of 8 above 8 the carry is 0.
            res = 0;
            carry = (count & 7) == 0 && (value & 0x01) != 0;
        }
        _state.CarryFlag = carry;
        UpdateFlags(res);
        // Real 386 SHL/SAL leaves OF documented "undefined" for count != 1
        // but in practice always computes OF = MSB(result) XOR CF. The
        // SingleStepTests reference vectors capture this exact behavior.
        _state.OverflowFlag = ((res & MsbMask) != 0) ^ _state.CarryFlag;
        return res;
    }

    public override byte Shld(byte destination, byte source, byte count) {
        throw new NotImplementedException("Shld is not available for 8bits operations");
    }

    public override byte Shrd(byte destination, byte source, byte count) {
        throw new NotImplementedException("Shrd is not available for 8bits operations");
    }

    public override byte Shr(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask;
        byte res;
        bool carry;
        if (count <= 8) {
            carry = ((value >> count - 1) & 0x1) == 1;
            res = (byte)(value >> count);
        } else {
            // Real 80386 byte SHR behavior for masked counts > 8: result is 0,
            // CF cycles back to MSB of the original at multiples of 8 (count
            // 16, 24); for non-multiples of 8 above 8 the carry is 0.
            res = 0;
            carry = (count & 7) == 0 && (value & MsbMask) != 0;
        }
        _state.CarryFlag = carry;
        UpdateFlags(res);
        _state.OverflowFlag = count == 1 && msb != 0;
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
        // Undocumented: real CPUs clear AF for logical operations
        _state.AuxiliaryFlag = false;
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
