namespace Spice86.Aeon.Emulator.Video.Registers;

using System.Runtime.CompilerServices;

public abstract class VgaRegisterBase {
    public virtual byte Value { get; set; }

    
    // protected byte GetBits(int start, int end)
    // {
    //     int bitCount = start - end + 1;
    //     int mask = (1 << bitCount) - 1;
    //     int shiftedValue = Value >> end;
    //     byte result = (byte)(shiftedValue & mask);
    //     return result;
    // }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected byte GetBits(int start, int end) => (byte)(Value >> end & (1 << start - end + 1) - 1);

    // protected void SetBits(int start, int end, byte newValue)
    // {
    //     int bitCount = start - end + 1;
    //     byte mask = (byte)((1 << bitCount) - 1);
    //     byte shiftedValue = (byte)((newValue & mask) << end);
    //     byte oldBitsMask = (byte)~(mask << end);
    //     Value = (byte)((Value & oldBitsMask) | shiftedValue);
    // }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetBits(int start, int end, byte newValue)
    {
        byte mask = (byte)((1 << start - end + 1) - 1);
        Value = (byte)(Value & (byte)~(mask << end) | (byte)((newValue & mask) << end));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool GetBit(int bit) {
        return (Value & 1 << bit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetBit(int bit, bool value) {
        int mask = 1 << bit;
        Value = (byte)(Value & ~mask | (value ? mask : 0x00));
    }
    
}