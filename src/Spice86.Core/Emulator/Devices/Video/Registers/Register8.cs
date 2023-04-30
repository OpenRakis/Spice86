namespace Spice86.Core.Emulator.Devices.Video.Registers;

using System.Runtime.CompilerServices;

public class Register8 {
    public virtual byte Value { get; set; }

    public bool this[int index] {
        get => GetBit(index);
        set => SetBit(index, value);
    }

    // protected byte GetBits(int start, int end)
    // {
    //     int bitCount = start - end + 1;
    //     int mask = (1 << bitCount) - 1;
    //     int shiftedValue = Value >> end;
    //     byte result = (byte)(shiftedValue & mask);
    //     return result;
    // }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetBits(int start, int end) {
        return (byte)(Value >> end & (1 << start - end + 1) - 1);
    }

    // protected void SetBits(int start, int end, byte newValue)
    // {
    //     int bitCount = start - end + 1;
    //     byte mask = (byte)((1 << bitCount) - 1);
    //     byte shiftedValue = (byte)((newValue & mask) << end);
    //     byte oldBitsMask = (byte)~(mask << end);
    //     Value = (byte)((Value & oldBitsMask) | shiftedValue);
    // }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBits(int start, int end, byte newValue) {
        byte mask = (byte)((1 << start - end + 1) - 1);
        Value = (byte)(Value & (byte)~(mask << end) | (byte)((newValue & mask) << end));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetBit(int bit) {
        return (Value & 1 << bit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBit(int bit, bool value) {
        int mask = 1 << bit;
        Value = (byte)(Value & ~mask | (value ? mask : 0x00));
    }
}