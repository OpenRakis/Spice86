namespace Spice86.Core.Emulator.CPU;

using System.Numerics;

/// <summary>
/// Arithmetic-logic unit
/// </summary>
public abstract class Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>
    where TUnsigned : IUnsignedNumber<TUnsigned>
    where TSigned : ISignedNumber<TSigned>
    where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
    where TSignedUpper : ISignedNumber<TSignedUpper> {
    /// <summary>
    /// Shifting this by the number we want to test gives 1 if number of bit is even and 0 if odd.<br/>
    /// Hardcoded numbers:<br/>
    /// 0 -> 0000: even -> 1<br/>
    /// 1 -> 0001: 1 bit so odd -> 0<br/>
    /// 2 -> 0010: 1 bit so odd -> 0<br/>
    /// 3 -> 0011: 2 bit so even -> 1<br/>
    /// 4 -> 0100: 1 bit so odd -> 0<br/>
    /// 5 -> 0101: even -> 1<br/>
    /// 6 -> 0110: even -> 1<br/>
    /// 7 -> 0111: odd -> 0<br/>
    /// 8 -> 1000: odd -> 0<br/>
    /// 9 -> 1001: even -> 1<br/>
    /// A -> 1010: even -> 1<br/>
    /// B -> 1011: odd -> 0<br/>
    /// C -> 1100: even -> 1<br/>
    /// D -> 1101: odd -> 0<br/>
    /// E -> 1110: odd -> 0<br/>
    /// F -> 1111: even -> 1<br/>
    /// => lookup table is 1001011001101001
    /// </summary>
    private const uint FourBitParityTable = 0b1001011001101001;

    protected const int ShiftCountMask = 0x1F;

    protected readonly State _state;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The state of the CPU.</param>
    public Alu(State state) {
        _state = state;
    }

    public TUnsigned Adc(TUnsigned value1, TUnsigned value2) {
        return Add(value1, value2, true);
    }

    public TUnsigned Add(TUnsigned value1, TUnsigned value2) {
        return Add(value1, value2, false);
    }

    public TUnsigned Inc(TUnsigned value) {
        // CF is not modified
        bool carry = _state.CarryFlag;
        TUnsigned res = Add(value, TUnsigned.One, false);
        _state.CarryFlag = carry;
        return res;
    }

    public abstract TUnsigned Add(TUnsigned value1, TUnsigned value2, bool useCarry);


    public TUnsigned Sbb(TUnsigned value1, TUnsigned value2) {
        return Sub(value1, value2, true);
    }

    public TUnsigned Sub(TUnsigned value1, TUnsigned value2) {
        return Sub(value1, value2, false);
    }

    public TUnsigned Dec(TUnsigned value1) {
        bool carry = _state.CarryFlag;
        TUnsigned res = Sub(value1, TUnsigned.One, false);
        _state.CarryFlag = carry;
        return res;
    }

    public abstract TUnsigned Sub(TUnsigned value1, TUnsigned value2, bool useCarry);

    public abstract TUnsigned Div(TUnsignedUpper value1, TUnsigned value2);
    public abstract TUnsignedUpper Mul(TUnsigned value1, TUnsigned value2);

    public abstract TSigned Idiv(TSignedUpper value1, TSigned value2);

    public abstract TSignedUpper Imul(TSigned value1, TSigned value2);


    public abstract TUnsigned And(TUnsigned value1, TUnsigned value2);
    public abstract TUnsigned Or(TUnsigned value1, TUnsigned value2);
    public abstract TUnsigned Xor(TUnsigned value1, TUnsigned value2);

    public abstract TUnsigned Rcl(TUnsigned value, byte count);

    public abstract TUnsigned Rcr(TUnsigned value, int count);

    public abstract TUnsigned Rol(TUnsigned value, byte count);

    public abstract TUnsigned Ror(TUnsigned value, int count);

    public abstract TUnsigned Sar(TUnsigned value, int count);

    public abstract TUnsigned Shl(TUnsigned value, int count);

    public abstract TUnsigned Shld(TUnsigned destination, TUnsigned source, byte count);

    public abstract TUnsigned Shr(TUnsigned value, int count);

    protected static uint BorrowBitsSub(uint value1, uint value2, uint dst) {
        return value1 ^ value2 ^ dst ^ ((value1 ^ dst) & (value1 ^ value2));
    }

    protected static uint CarryBitsAdd(uint value1, uint value2, uint dst) {
        return value1 ^ value2 ^ dst ^ ((value1 ^ dst) & ~(value1 ^ value2));
    }

    private static bool IsParity(byte value) {
        int low4 = value & 0xF;
        int high4 = value >> 4 & 0xF;
        return (FourBitParityTable >> low4 & 1) == (FourBitParityTable >> high4 & 1);
    }

    // from https://www.vogons.org/viewtopic.php?t=55377
    protected static uint OverflowBitsAdd(uint value1, uint value2, uint dst) {
        return (value1 ^ dst) & ~(value1 ^ value2);
    }

    protected static uint OverflowBitsSub(uint value1, uint value2, uint dst) {
        return (value1 ^ dst) & (value1 ^ value2);
    }

    protected void SetCarryFlagForRightShifts(uint value, int count) {
        uint lastBit = value >> count - 1 & 0x1;
        _state.CarryFlag = lastBit == 1;
    }

    protected void SetParityFlag(ulong value) {
        _state.ParityFlag = IsParity((byte)value);
    }

    protected void SetZeroFlag(ulong value) {
        _state.ZeroFlag = value == 0;
    }

    protected abstract void SetSignFlag(TUnsigned value);

    public void UpdateFlags(TUnsigned value) {
        SetZeroFlag(ulong.CreateTruncating(value));
        SetParityFlag(ulong.CreateTruncating(value));
        SetSignFlag(value);
    }
}