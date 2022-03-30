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
    private const ushort BeforeMsbMask16 = 0x4000;

    private const byte BeforeMsbMask8 = 0x40;

    private const uint FourBitParityTable = 0b1001011001101001;

    private const ushort MsbMask16 = 0x8000;

    private const byte MsbMask8 = 0x80;

    private const int ShiftCountMask = 0x1F;

    private readonly State _state;

    public Alu(State state) {
        _state = state;
    }

    public ushort Adc16(ushort value1, ushort value2) {
        return Add16(value1, value2, true);
    }

    public byte Adc8(byte value1, byte value2) {
        return Add8(value1, value2, true);
    }

    public ushort Add16(ushort value1, ushort value2, bool useCarry) {
        int carry = (useCarry && _state.CarryFlag) ? 1 : 0;
        ushort res = (ushort)(value1 + value2 + carry);
        UpdateFlags16(res);
        uint carryBits = CarryBitsAdd(value1, value2, res);
        uint overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.CarryFlag = ((carryBits >> 15) & 1) == 1;
        _state.AuxiliaryFlag = ((carryBits >> 3) & 1) == 1;
        _state.OverflowFlag = ((overflowBits >> 15) & 1) == 1;
        return res;
    }

    public ushort Add16(ushort value1, ushort value2) {
        return Add16(value1, value2, false);
    }

    public byte Add8(byte value1, byte value2, bool useCarry) {
        int carry = (useCarry && _state.CarryFlag) ? 1 : 0;
        byte res = (byte)(value1 + value2 + carry);
        UpdateFlags8(res);
        uint carryBits = CarryBitsAdd(value1, value2, res);
        uint overflowBits = OverflowBitsAdd(value1, value2, res);
        _state.CarryFlag = ((carryBits >> 7) & 1) == 1;
        _state.AuxiliaryFlag = ((carryBits >> 3) & 1) == 1;
        _state.OverflowFlag = ((overflowBits >> 7) & 1) == 1;
        return res;
    }

    public byte Add8(byte value1, byte value2) {
        return Add8(value1, value2, false);
    }

    public ushort And16(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 & value2);
        UpdateFlags16(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public byte And8(byte value1, byte value2) {
        byte res = (byte)(value1 & value2);
        UpdateFlags8(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public ushort Dec16(ushort value1) {
        bool carry = _state.CarryFlag;
        ushort res = Sub16(value1, 1, false);
        _state.CarryFlag = carry;
        return res;
    }

    public byte Dec8(byte value1) {
        bool carry = _state.CarryFlag;
        byte res = Sub8(value1, 1, false);
        _state.CarryFlag = carry;
        return res;
    }

    public static ushort? Div16(uint value1, ushort value2) {
        if (value2 == 0) {
            return null;
        }
        uint res = value1 / value2;
        if (res > 0xFFFF) {
            return null;
        }
        return (ushort)res;
    }

    public static byte? Div8(ushort value1, byte value2) {
        if (value2 == 0) {
            return null;
        }
        uint res = (uint)(value1 / value2);
        if (res > 0xFF) {
            return null;
        }
        return (byte)res;
    }

    public static short? Idiv16(int value1, short value2) {
        if (value2 == 0) {
            return null;
        }

        long res = value1 / value2;
        unchecked {
            if (res is > 0x7FFF or < ((short)0x8000)) {
                return null;
            }
        }

        return (short)res;
    }

    public static sbyte? Idiv8(short value1, sbyte value2) {
        if (value2 == 0) {
            return null;
        }

        int res = value1 / value2;
        unchecked {
            if (res is > 0x7F or < ((sbyte)0x80)) {
                return null;
            }
        }

        return (sbyte)res;
    }

    public int Imul16(short value1, short value2) {
        int res = value1 * value2;
        bool doesNotFitInWord = res != (short)(res);
        _state.OverflowFlag = doesNotFitInWord;
        _state.CarryFlag = doesNotFitInWord;
        return res;
    }

    public short Imul8(sbyte value1, sbyte value2) {
        int res = value1 * value2;
        bool doesNotFitInByte = res != (sbyte)(res);
        _state.OverflowFlag = doesNotFitInByte;
        _state.CarryFlag = doesNotFitInByte;
        return (short)res;
    }

    public ushort Inc16(ushort value) {
        // CF is not modified
        bool carry = _state.CarryFlag;
        ushort res = Add16(value, 1, false);
        _state.CarryFlag = carry;
        return res;
    }

    public byte Inc8(byte value) {
        // CF is not modified
        bool carry = _state.CarryFlag;
        byte res = Add8(value, 1, false);
        _state.CarryFlag = carry;
        return res;
    }

    public uint Mul16(uint value1, uint value2) {
        uint res = value1 * value2;
        bool upperHalfNonZero = (res & 0xFFFF0000) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag16((ushort)res);
        return res;
    }

    public ushort Mul8(byte value1, byte value2) {
        ushort res = (ushort)(value1 * value2);
        bool upperHalfNonZero = (res & 0xFF00) != 0;
        _state.OverflowFlag = upperHalfNonZero;
        _state.CarryFlag = upperHalfNonZero;
        SetZeroFlag(res);
        SetParityFlag(res);
        SetSignFlag8((byte)res);
        return res;
    }

    public ushort Or16(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 | value2);
        UpdateFlags16(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public byte Or8(byte value1, byte value2) {
        byte res = (byte)(value1 | value2);
        UpdateFlags8(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public ushort Rcl16(ushort value, int count) {
        count = (count & ShiftCountMask) % 17;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (16 - count)) & 0x1;
        ushort res = (ushort)(value << count);
        int mask = (1 << (count - 1)) - 1;
        res = (ushort)(res | ((value >> (17 - count)) & mask));
        if (_state.CarryFlag) {
            res = (ushort)(res | (1 << (count - 1)));
        }
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask16) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public byte Rcl8(byte value, int count) {
        count = (count & ShiftCountMask) % 9;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (8 - count)) & 0x1;
        byte res = (byte)((value << count));
        int mask = (1 << (count - 1)) - 1;
        res = (byte)(res | ((value >> (9 - count)) & mask));
        if (_state.CarryFlag) {
            res = (byte)(res | (1 << (count - 1)));
        }
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask8) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public ushort Rcr16(ushort value, int count) {
        count = (count & ShiftCountMask) % 17;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (16 - count)) - 1;
        ushort res = (ushort)((value >> count) & mask);
        res = (ushort)(res | (value << (17 - count)));
        if (_state.CarryFlag) {
            res = (ushort)(res | (1 << (16 - count)));
        }
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public byte Rcr8(byte value, int count) {
        count = (count & ShiftCountMask) % 9;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (8 - count)) - 1;
        byte res = (byte)((value >> count) & mask);
        res = (byte)(res | (value << (9 - count)));
        if (_state.CarryFlag) {
            res = (byte)(res | (1 << (8 - count)));
        }
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public ushort Rol16(ushort value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (16 - count)) & 0x1;
        ushort res = (ushort)((value << count));
        res = (ushort)(res | (value >> (16 - count)));
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask16) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public byte Rol8(byte value, int count) {
        count = (count & ShiftCountMask) % 8;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (8 - count)) & 0x1;
        byte res = (byte)(value << count);
        res = (byte)(res | (value >> (8 - count)));
        _state.CarryFlag = carry != 0;
        bool msb = (res & MsbMask8) != 0;
        _state.OverflowFlag = msb ^ _state.CarryFlag;
        return res;
    }

    public ushort Ror16(ushort value, int count) {
        count = (count & ShiftCountMask) % 16;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (16 - count)) - 1;
        ushort res = (ushort)((value >> count) & mask);
        res = (ushort)(res | (value << (16 - count)));
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate16(res);
        return res;
    }

    public byte Ror8(byte value, int count) {
        count = (count & ShiftCountMask) % 8;
        if (count == 0) {
            return value;
        }

        int carry = (value >> (count - 1)) & 0x1;
        int mask = (1 << (8 - count)) - 1;
        byte res = (byte)((value >> count) & mask);
        res = (byte)(res | (value << (8 - count)));
        _state.CarryFlag = carry != 0;
        SetOverflowForRigthRotate8(res);
        return res;
    }

    public ushort Sar16(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        short res = (short)value;
        SetCarryFlagForRightShifts(res, count);
        res >>= count;
        UpdateFlags16((ushort)res);
        _state.OverflowFlag = false;
        return (ushort)res;
    }

    public byte Sar8(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }
        sbyte res = (sbyte)value;
        SetCarryFlagForRightShifts(res, count);
        res >>= count;
        UpdateFlags8((byte)res);
        _state.OverflowFlag = false;
        return (byte)res;
    }

    public ushort Sbb16(ushort value1, ushort value2) {
        return Sub16(value1, value2, true);
    }

    public byte Sbb8(byte value1, byte value2) {
        return Sub8(value1, value2, true);
    }

    public ushort Shl16(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = (value << (count - 1)) & MsbMask16;
        _state.CarryFlag = msbBefore != 0;
        ushort res = (ushort)(value << count);
        UpdateFlags16(res);
        int msb = res & MsbMask16;
        _state.OverflowFlag = (msb ^ msbBefore) != 0;
        return res;
    }

    public byte Shl8(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msbBefore = (value << (count - 1)) & MsbMask8;
        _state.CarryFlag = msbBefore != 0;
        byte res = (byte)(value << count);
        UpdateFlags8(res);
        int msb = res & MsbMask8;
        _state.OverflowFlag = (msb ^ msbBefore) != 0;
        return res;
    }

    public ushort Shr16(ushort value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask16;
        _state.OverflowFlag = msb != 0;
        SetCarryFlagForRightShifts(value, count);
        ushort res = (ushort)(value >> count);
        UpdateFlags16(res);
        return res;
    }

    public byte Shr8(byte value, int count) {
        count &= ShiftCountMask;
        if (count == 0) {
            return value;
        }

        int msb = value & MsbMask8;
        _state.OverflowFlag = msb != 0;
        SetCarryFlagForRightShifts(value, count);
        byte res = (byte)(value >> count);
        UpdateFlags8(res);
        return res;
    }

    public ushort Sub16(ushort value1, ushort value2) {
        return Sub16(value1, value2, false);
    }

    public ushort Sub16(ushort value1, ushort value2, bool useCarry) {
        int carry = (useCarry && _state.CarryFlag) ? 1 : 0;
        ushort res = (ushort)(value1 - value2 - carry);
        UpdateFlags16(res);
        uint borrowBits = BorrowBitsSub(value1, value2, res);
        uint overflowBits = OverflowBitsSub(value1, value2, res);
        _state.CarryFlag = ((borrowBits >> 15) & 1) == 1;
        _state.AuxiliaryFlag = ((borrowBits >> 3) & 1) == 1;
        _state.OverflowFlag = ((overflowBits >> 15) & 1) == 1;
        return res;
    }

    public byte Sub8(byte value1, byte value2) {
        return Sub8(value1, value2, false);
    }

    public byte Sub8(byte value1, byte value2, bool useCarry) {
        int carry = (useCarry && _state.CarryFlag) ? 1 : 0;
        byte res = (byte)(value1 - value2 - carry);
        UpdateFlags8(res);
        uint borrowBits = BorrowBitsSub(value1, value2, res);
        uint overflowBits = OverflowBitsSub(value1, value2, res);
        _state.CarryFlag = ((borrowBits >> 7) & 1) == 1;
        _state.AuxiliaryFlag = ((borrowBits >> 3) & 1) == 1;
        _state.OverflowFlag = ((overflowBits >> 7) & 1) == 1;
        return res;
    }

    public void UpdateFlags16(ushort value) {
        SetZeroFlag(value);
        SetParityFlag(value);
        SetSignFlag16(value);
    }

    public void UpdateFlags8(byte value) {
        SetZeroFlag(value);
        SetParityFlag(value);
        SetSignFlag8(value);
    }

    public ushort Xor16(ushort value1, ushort value2) {
        ushort res = (ushort)(value1 ^ value2);
        UpdateFlags16(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    public byte Xor8(byte value1, byte value2) {
        byte res = (byte)(value1 ^ value2);
        UpdateFlags8(res);
        _state.CarryFlag = false;
        _state.OverflowFlag = false;
        return res;
    }

    private static uint BorrowBitsSub(uint value1, uint value2, uint dst) {
        return ((value1 ^ value2 ^ dst) ^ ((value1 ^ dst) & (value1 ^ value2)));
    }

    private static uint CarryBitsAdd(uint value1, uint value2, uint dst) {
        return ((value1 ^ value2 ^ dst) ^ ((value1 ^ dst) & (~(value1 ^ value2))));
    }

    private static bool IsParity(byte value) {
        int low4 = value & 0xF;
        int high4 = (value >> 4) & 0xF;
        return ((FourBitParityTable >> low4) & 1) == ((FourBitParityTable >> high4) & 1);
    }

    // from https://www.vogons.org/viewtopic.php?t=55377
    private static uint OverflowBitsAdd(uint value1, uint value2, uint dst) {
        return (value1 ^ dst) & (~(value1 ^ value2));
    }

    private static uint OverflowBitsSub(uint value1, uint value2, uint dst) {
        return (value1 ^ dst) & (value1 ^ value2);
    }

    private void SetCarryFlagForRightShifts(int value, int count) {
        int lastBit = (value >> (count - 1)) & 0x1;
        _state.CarryFlag = lastBit == 1;
    }

    private void SetOverflowForRigthRotate16(ushort res) {
        bool msb = (res & MsbMask16) != 0;
        bool beforeMsb = (res & BeforeMsbMask16) != 0;
        _state.OverflowFlag = msb ^ beforeMsb;
    }

    private void SetOverflowForRigthRotate8(byte res) {
        bool msb = (res & MsbMask8) != 0;
        bool beforeMsb = (res & BeforeMsbMask8) != 0;
        _state.OverflowFlag = msb ^ beforeMsb;
    }

    private void SetParityFlag(uint value) {
        _state.ParityFlag = IsParity((byte)value);
    }

    private void SetSignFlag16(ushort value) {
        _state.SignFlag = (value & MsbMask16) != 0;
    }

    private void SetSignFlag8(byte value) {
        _state.SignFlag = (value & MsbMask8) != 0;
    }

    private void SetZeroFlag(uint value) {
        _state.ZeroFlag = value == 0;
    }
}