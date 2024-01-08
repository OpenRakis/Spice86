namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

public class Alu32 : Alu<uint, int, ulong, long>  {

    private const uint BeforeMsbMask = 0x40000000;

    private const uint MsbMask = 0x80000000;

    public Alu32(State state) : base(state) {
    }

    public override uint Add(uint value1, uint value2, bool useCarry) {
        int carry = useCarry && _state.CarryFlag ? 1 : 0;
        uint res = (uint)(value1 + value2 + carry);
        UpdateFlags(res);
        uint carryBits = CarryBitsAdd(value1, value2, res);
        uint overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.CarryFlag = (carryBits >> 31 & 1) == 1;
        _state.AuxiliaryFlag = (carryBits >> 3 & 1) == 1;
        _state.OverflowFlag = (overflowBits >> 31 & 1) == 1;
        return res;
    }

    public override uint And(uint value1, uint value2) {
        uint res = value1 & value2;
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public override uint Div(ulong value1, uint value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        ulong res = value1 / value2;
        if (res > uint.MaxValue) {
            throw new CpuDivisionErrorException($"Division result out of range: {res}");
        }

        return (uint)res;
    }

    public override int Idiv(long value1, int value2) {
        if (value2 == 0) {
            throw new CpuDivisionErrorException($"Division by zero");
        }

        long res = value1 / value2;
        unchecked {
            if (res is > 0x7FFFFFFF or < (int)0x80000000) {
                throw new CpuDivisionErrorException($"Division result out of range: {res}");
            }
        }

        return (int)res;
    }

    public override long Imul(int value1, int value2) {
        long res = (long)value1 * value2;
        bool doesNotFitInDWord = res != (int)res;
        _state.OverflowFlag = doesNotFitInDWord;
        _state.CarryFlag = doesNotFitInDWord;
        return res;
    }

    public override ulong Mul(uint value1, uint value2) {
        ulong res = (ulong)value1 * value2;
        bool upperHalfNonZero = (res & 0xFFFFFFFF00000000) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag((uint)res);
        return res;
    }

    public override uint Or(uint value1, uint value2) {
        uint res = value1 | value2;
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public override uint Rcl(uint value, byte count) {
        count = (byte) ((count & ShiftCountMask) % 33);
        if (count == 0) {
            return value;
        }

        uint carry = value >> 32 - count & 0x1;
        uint res = value << count;
        int mask = (1 << count - 1) - 1;
        res = (uint)(res | (value >> 33 - count & mask));
        if (_state.CarryFlag) {
            res = (uint)(res | 1 << count - 1);
        }

        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public override uint Rcr(uint value, int count) {
        count = (count & ShiftCountMask) % 33;
        if (count == 0) {
            return value;
        }

        uint carry = value >> count - 1 & 0x1;
        int mask = (1 << 32 - count) - 1;
        uint res = (uint) (value >> count & mask);
        res |= value << 33 - count;
        if (_state.CarryFlag) {
            res = (ushort)(res | 1 << 32 - count);
        }

        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate32(res);
        return res;
    }

    public override uint Rol(uint value, byte count) {
        count = (byte) ((count & ShiftCountMask) % 32);
        if (count == 0) {
            return value;
        }

        uint carry = value >> 32 - count & 0x1;
        uint res = value << count;
        res |= value >> 32 - count;
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public override uint Ror(uint value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        uint carry = value >> count - 1 & 0x1;
        int mask = (1 << 32 - count) - 1;
        uint res = (uint)(value >> count & mask);
        res |= value << 32 - count;
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate32(res);
        return res;
    }

    public override uint Sar(uint value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int res = (int)value;
        SetCarryFlagForRightShifts((uint)res, count);
        res >>= count;
        UpdateFlags((uint)res);
        _state.OverflowFlag = false;
        return (uint)res;
    }

    public override uint Shl(uint value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        uint msbBefore = (value << (count - 1)) & MsbMask;
        _state.CarryFlag = msbBefore != 0;
        uint res = value << count;
        UpdateFlags(res);
        uint msb = res & MsbMask;
        _state.OverflowFlag = (msb ^ msbBefore) != 0;
        return res;
    }

    public override uint Shld(uint destination, uint source, byte count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return destination;
        }

        uint msbBefore = destination & MsbMask;
        _state.CarryFlag = (destination >> (32 - count) & 1) != 0;
        uint res = (destination << count) | (source >> (32 - count));
        UpdateFlags(res);
        uint msb = res & MsbMask;
        _state.OverflowFlag = msb != msbBefore;
        return res;
    }

    public override uint Shr(uint value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        uint msb = value & MsbMask;
        _state.OverflowFlag = msb != 0;
        SetCarryFlagForRightShifts(value, count);
        uint res = value >> count;
        UpdateFlags(res);
        return res;
    }

    public override uint Sub(uint value1, uint value2, bool useCarry) {
        int carry = (useCarry && _state.CarryFlag) ? 1 : 0;
        uint res = (uint)(value1 - value2 - carry);
        UpdateFlags(res);
        uint borrowBits = BorrowBitsSub(value1, value2, res);
        uint overflowBits = OverflowBitsSub(value1, value2, res);
        _state.CarryFlag = ((borrowBits >> 31) & 1) == 1;
        _state.AuxiliaryFlag = ((borrowBits >> 3) & 1) == 1;
        _state.OverflowFlag = ((overflowBits >> 31) & 1) == 1;
        return res;
    }

    
    public override uint Xor(uint value1, uint value2) {
        uint res = value1 ^ value2;
        UpdateFlags(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    private void SetOverflowForRigthRotate32(uint res) {
        bool msb = (res & MsbMask) != 0;
        bool beforeMsb = (res & BeforeMsbMask) != 0;
        _state.OverflowFlag = msb ^ beforeMsb;
    }

    protected override void SetSignFlag(uint value) {
        _state.SignFlag = (value & MsbMask) != 0;
    }
}