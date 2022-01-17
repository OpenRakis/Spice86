namespace Spice86.Emulator.CPU;

public class Alu {
    /**
     * Shifting this by the number we want to test gives 1 if number of bit is even and 0 if odd.<br/>
     * Hardcoded numbers:<br/>
     * 0 -> 0000: even -> 1<br/>
     * 1 -> 0001: 1 bit so odd -> 0<br/>
     * 2 -> 0010: 1 bit so odd -> 0<br/>
     * 3 -> 0011: 2 bit so even -> 1<br/>
     * 4 -> 0100: 1 bit so odd -> 0<br/>
     * 5 -> 0101: even -> 1<br/>
     * 6 -> 0110: even -> 1<br/>
     * 7 -> 0111: odd -> 0<br/>
     * 8 -> 1000: odd -> 0<br/>
     * 9 -> 1001: even -> 1<br/>
     * A -> 1010: even -> 1<br/>
     * B -> 1011: odd -> 0<br/>
     * C -> 1100: even -> 1<br/>
     * D -> 1101: odd -> 0<br/>
     * E -> 1110: odd -> 0<br/>
     * F -> 1111: even -> 1<br/>
     * => lookup table is 1001011001101001
     */

    private const int BeforeMsbMask16 = 0x4000;

    private const int BeforeMsbMask8 = 0x40;

    private const int FourBitParityTable = 0b1001011001101001;

    private const int MsbMask16 = 0x8000;

    private const int MsbMask8 = 0x80;

    private const int ShiftCountMask = 0x1F;

    private readonly State _state;

    public Alu(State state) {
        this._state = state;
    }

    public int Adc16(int value1, int value2) {
        return Add16(value1, value2, true);
    }

    public int Adc8(int value1, int value2) {
        return Add8(value1, value2, true);
    }

    public int Add16(int value1, int value2, bool useCarry) {
        int carry = (useCarry && _state.GetCarryFlag()) ? 1 : 0;
        int res = (ushort)(value1 + value2 + carry);
        UpdateFlags16(res);
        int carryBits = CarryBitsAdd(value1, value2, res);
        int overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.SetCarryFlag(((carryBits >> 15) & 1) == 1);
        _state.SetAuxiliaryFlag(((carryBits >> 3) & 1) == 1);
        _state.SetOverflowFlag(((overflowBits >> 15) & 1) == 1);
        return res;
    }

    public int Add16(int value1, int value2) {
        return Add16(value1, value2, false);
    }

    public int Add8(int value1, int value2, bool useCarry) {
        int carry = (useCarry && _state.GetCarryFlag()) ? 1 : 0;
        var res = (byte)(value1 + value2 + carry);
        UpdateFlags8(res);
        int carryBits = CarryBitsAdd(value1, value2, res);
        int overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.SetCarryFlag(((carryBits >> 7) & 1) == 1);
        _state.SetAuxiliaryFlag(((carryBits >> 3) & 1) == 1);
        _state.SetOverflowFlag(((overflowBits >> 7) & 1) == 1);
        return res;
    }

    public int Add8(int value1, int value2) {
        return Add8(value1, value2, false);
    }

    public int And16(int value1, int value2) {
        int res = value1 & value2;
        UpdateFlags16(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int And8(int value1, int value2) {
        int res = value1 & value2;
        UpdateFlags8(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Dec16(int value1) {
        bool carry = _state.GetCarryFlag();
        int res = Sub16(value1, 1, false);
        _state.SetCarryFlag(carry);
        return res;
    }

    public int Dec8(int value1) {
        bool carry = _state.GetCarryFlag();
        int res = Sub8(value1, 1, false);
        _state.SetCarryFlag(carry);
        return res;
    }

    public int? Div16(int value1, int value2) {
        if (value2 == 0) {
            return null;
        }

        long res = ((uint)(value1) / value2);
        if (res > 0xFFFF) {
            return null;
        }

        return (int)res;
    }

    public int? Div8(int value1, int value2) {
        if (value2 == 0) {
            return null;
        }

        int res = value1 / value2;
        if (res > 0xFF) {
            return null;
        }

        return res;
    }

    public int? Idiv16(int value1, int value2) {
        if (value2 == 0) {
            return null;
        }

        int res = value1 / (short)(value2);
        unchecked {
            if ((res > 0x7FFF) || (res < (short)0x8000)) {
                return null;
            }
        }

        return res;
    }

    public int? Idiv8(int value1, int value2) {
        if (value2 == 0) {
            return null;
        }

        int res = (short)(value1) / (sbyte)(value2);
        if ((res > 0x7F) || (res < (byte)0x80)) {
            return null;
        }

        return res;
    }

    public int Imul16(int value1, int value2) {
        int res = (short)(value1) * (short)(value2);
        bool doesNotFitInWord = res != (short)(res);
        _state.SetOverflowFlag(doesNotFitInWord);
        _state.SetCarryFlag(doesNotFitInWord);
        return res;
    }

    public int Imul8(int value1, int value2) {
        int res = (sbyte)(value1) * (sbyte)(value2);
        bool doesNotFitInByte = res != (sbyte)(res);
        _state.SetOverflowFlag(doesNotFitInByte);
        _state.SetCarryFlag(doesNotFitInByte);
        return res;
    }

    public int Inc16(int value) {
        // CF is not modified
        bool carry = _state.GetCarryFlag();
        int res = Add16(value, 1, false);
        _state.SetCarryFlag(carry);
        return res;
    }

    public int Inc8(int value) {
        // CF is not modified
        bool carry = _state.GetCarryFlag();
        int res = Add8(value, 1, false);
        _state.SetCarryFlag(carry);
        return res;
    }

    public int Mul16(int value1, int value2) {
        int res = value1 * value2;
        bool upperHalfNonZero = (res & 0xFFFF0000) != 0;
        _state.SetOverflowFlag(upperHalfNonZero);
        _state.SetCarryFlag(upperHalfNonZero);
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag16(res);
        return res;
    }

    public int Mul8(int value1, int value2) {
        int res = value1 * value2;
        bool upperHalfNonZero = (res & 0xFF00) != 0;
        _state.SetOverflowFlag(upperHalfNonZero);
        _state.SetCarryFlag(upperHalfNonZero);
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag8(res);
        return res;
    }

    public int Or16(int value1, int value2) {
        int res = value1 | value2;
        UpdateFlags16(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Or8(int value1, int value2) {
        int res = value1 | value2;
        UpdateFlags8(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Rcl16(int value, int count) {
        count = (count & ShiftCountMask) % 17;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (16 - count)) & 0x1;
        int res = (value << count);
        int mask = (1 << (count - 1)) - 1;
        res |= (value >> (17 - count)) & mask;
        if (_state.GetCarryFlag()) {
            res |= 1 << (count - 1);
        }

        res = (short)(res);
        _state.SetCarryFlag(carry != 0);
        bool msb = (res & MsbMask16) != 0;
        _state.SetOverflowFlag(msb ^ _state.GetCarryFlag());
        return res;
    }

    public int Rcl8(int value, int count) {
        count = (count & ShiftCountMask) % 9;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (8 - count)) & 0x1;
        int res = (value << count);
        int mask = (1 << (count - 1)) - 1;
        res |= (value >> (9 - count)) & mask;
        if (_state.GetCarryFlag()) {
            res |= 1 << (count - 1);
        }

        res = (byte)(res);
        _state.SetCarryFlag(carry != 0);
        bool msb = (res & MsbMask8) != 0;
        _state.SetOverflowFlag(msb ^ _state.GetCarryFlag());
        return res;
    }

    public int Rcr16(int value, int count) {
        count = (count & ShiftCountMask) % 17;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (16 - count)) - 1;
        int res = (value >> count) & mask;
        res |= (value << (17 - count));
        if (_state.GetCarryFlag()) {
            res |= 1 << (16 - count);
        }

        res = (short)(res);
        _state.SetCarryFlag(carry != 0);
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public int Rcr8(int value, int count) {
        count = (count & ShiftCountMask) % 9;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (8 - count)) - 1;
        int res = (value >> count) & mask;
        res |= (value << (9 - count));
        if (_state.GetCarryFlag()) {
            res |= 1 << (8 - count);
        }

        res = (byte)(res);
        _state.SetCarryFlag(carry != 0);
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public int Rol16(int value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (16 - count)) & 0x1;
        int res = (value << count);
        res |= (value >> (16 - count));
        res = (short)(res);
        _state.SetCarryFlag(carry != 0);
        bool msb = (res & MsbMask16) != 0;
        _state.SetOverflowFlag(msb ^ _state.GetCarryFlag());
        return res;
    }

    public int Rol8(int value, int count) {
        count = (count & ShiftCountMask) % 8;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (8 - count)) & 0x1;
        int res = (value << count);
        res |= (value >> (8 - count));
        res = (byte)(res);
        _state.SetCarryFlag(carry != 0);
        bool msb = (res & MsbMask8) != 0;
        _state.SetOverflowFlag(msb ^ _state.GetCarryFlag());
        return res;
    }

    public int Ror16(int value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (16 - count)) - 1;
        int res = (value >> count) & mask;
        res |= (value << (16 - count));
        res = (short)(res);
        _state.SetCarryFlag(carry != 0);
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public int Ror8(int value, int count) {
        count = (count & ShiftCountMask) % 8;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (8 - count)) - 1;
        int res = (value >> count) & mask;
        res |= (value << (8 - count));
        res = (byte)(res);
        _state.SetCarryFlag(carry != 0);
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public int Sar16(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int res = (short)(value);
        SetCarryFlagForRightShifts(res, count);
        res >>= count;
        res = (short)(res);
        UpdateFlags16(res);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Sar8(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int res = (sbyte)(value);
        SetCarryFlagForRightShifts(res, count);
        res >>= count;
        res = (byte)(res);
        UpdateFlags8(res);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Sbb16(int value1, int value2) {
        return Sub16(value1, value2, true);
    }

    public int Sbb8(int value1, int value2) {
        return Sub8(value1, value2, true);
    }

    public int Shl16(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = (value << (count - 1)) & MsbMask16;
        _state.SetCarryFlag(msbBefore != 0);
        int res = value << count;
        res = (short)(res);
        UpdateFlags16(res);
        int msb = res & MsbMask16;
        _state.SetOverflowFlag((msb ^ msbBefore) != 0);
        return res;
    }

    public int Shl8(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = (value << (count - 1)) & MsbMask8;
        _state.SetCarryFlag(msbBefore != 0);
        int res = value << count;
        res = (byte)(res);
        UpdateFlags8(res);
        int msb = res & MsbMask8;
        _state.SetOverflowFlag((msb ^ msbBefore) != 0);
        return res;
    }

    public int Shr16(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask16;
        _state.SetOverflowFlag(msb != 0);
        SetCarryFlagForRightShifts(value, count);
        int res = value >> count;
        res = (short)(res);
        UpdateFlags16(res);
        return res;
    }

    public int Shr8(int value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask8;
        _state.SetOverflowFlag(msb != 0);
        SetCarryFlagForRightShifts(value, count);
        int res = value >> count;
        res = (byte)(res);
        UpdateFlags8(res);
        return res;
    }

    public int Sub16(int value1, int value2) {
        return Sub16(value1, value2, false);
    }

    public int Sub16(int value1, int value2, bool useCarry) {
        int carry = (useCarry && _state.GetCarryFlag()) ? 1 : 0;
        int res = (ushort)(value1 - value2 - carry);
        UpdateFlags16(res);
        int borrowBits = BorrowBitsSub(value1, value2, res);
        int overflowBits = OverflowBitsSub(value1, value2, res);
        _state.SetCarryFlag(((borrowBits >> 15) & 1) == 1);
        _state.SetAuxiliaryFlag(((borrowBits >> 3) & 1) == 1);
        _state.SetOverflowFlag(((overflowBits >> 15) & 1) == 1);
        return res;
    }

    public int Sub8(int value1, int value2) {
        return Sub8(value1, value2, false);
    }

    public int Sub8(int value1, int value2, bool useCarry) {
        int carry = (useCarry && _state.GetCarryFlag()) ? 1 : 0;
        int res = (byte)(value1 - value2 - carry);
        UpdateFlags8(res);
        int borrowBits = BorrowBitsSub(value1, value2, res);
        int overflowBits = OverflowBitsSub(value1, value2, res);
        _state.SetCarryFlag(((borrowBits >> 7) & 1) == 1);
        _state.SetAuxiliaryFlag(((borrowBits >> 3) & 1) == 1);
        _state.SetOverflowFlag(((overflowBits >> 7) & 1) == 1);
        return res;
    }

    public void UpdateFlags16(int value) {
        SetZeroFlag(value);
        SetParityFlag(value);
        SetSignFlag16(value);
    }

    public void UpdateFlags8(int value) {
        SetZeroFlag(value);
        SetParityFlag(value);
        SetSignFlag8(value);
    }

    public int Xor16(int value1, int value2) {
        int res = value1 ^ value2;
        UpdateFlags16(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    public int Xor8(int value1, int value2) {
        int res = value1 ^ value2;
        UpdateFlags8(res);
        _state.SetCarryFlag(false);
        _state.SetOverflowFlag(false);
        return res;
    }

    private static int BorrowBitsSub(int value1, int value2, int dst) {
        return (((value1 ^ value2) ^ dst) ^ ((value1 ^ dst) & (value1 ^ value2)));
    }

    private static int CarryBitsAdd(int value1, int value2, int dst) {
        return (((value1 ^ value2) ^ dst) ^ ((value1 ^ dst) & (~(value1 ^ value2))));
    }

    private static bool IsParity(int value) {
        int low4 = value & 0xF;
        int high4 = (value >> 4) & 0xF;
        return ((FourBitParityTable >> low4) & 1) == ((FourBitParityTable >> high4) & 1);
    }

    // from https://www.vogons.org/viewtopic.php?t=55377
    private static int OverflowBitsAdd(int value1, int value2, int dst) {
        return ((value1 ^ dst) & (~(value1 ^ value2)));
    }

    private static int OverflowBitsSub(int value1, int value2, int dst) {
        return ((value1 ^ dst) & (value1 ^ value2));
    }

    private void SetCarryFlagForRightShifts(int value, int count) {
        int lastBit = (value >> (count - 1)) & 0x1;
        _state.SetCarryFlag(lastBit == 1);
    }

    private void SetOverflowForRigthRotate16(int res) {
        bool msb = (res & MsbMask16) != 0;
        bool beforeMsb = (res & BeforeMsbMask16) != 0;
        _state.SetOverflowFlag(msb ^ beforeMsb);
    }

    private void SetOverflowForRigthRotate8(int res) {
        bool msb = (res & MsbMask8) != 0;
        bool beforeMsb = (res & BeforeMsbMask8) != 0;
        _state.SetOverflowFlag(msb ^ beforeMsb);
    }

    private void SetParityFlag(int value) {
        _state.SetParityFlag(IsParity((byte)(value)));
    }

    private void SetSignFlag16(int value) {
        _state.SetSignFlag((value & MsbMask16) != 0);
    }

    private void SetSignFlag8(int value) {
        _state.SetSignFlag((value & MsbMask8) != 0);
    }

    private void SetZeroFlag(int value) {
        _state.SetZeroFlag(value == 0);
    }
}