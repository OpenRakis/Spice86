namespace Spice86.Core.Emulator.Devices.Video.Registers;

using System.Runtime.CompilerServices;

/// <summary>
/// Represents an 8-bit register.
/// </summary>
public class Register8 {
    /// <summary>
    /// Gets or sets the value of the register.
    /// </summary>
    public virtual byte Value { get; set; }
    /// <summary>
    /// Indexer for the bits of the register.
    /// </summary>
    /// <param name="index">Which bit to get or set in the <see cref="Value"/> byte</param>
    /// <returns>The boolean value of the register at the given index</returns>

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
    /// <summary>
    /// Gets the bits in the specified range from the <see cref="Value"/> property.
    /// </summary>
    /// <param name="start">the start index</param>
    /// <param name="end">the inclusive end index</param>
    /// <returns>The bits at the specified range</returns>
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
    /// <summary>
    /// Flips the bits in the specified range to the specified value.
    /// </summary>
    /// <param name="start">The start index</param>
    /// <param name="end">The inclusive end index</param>
    /// <param name="newValue">The value to apply to each bit</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBits(int start, int end, byte newValue) {
        byte mask = (byte)((1 << start - end + 1) - 1);
        Value = (byte)(Value & (byte)~(mask << end) | (byte)((newValue & mask) << end));
    }

    /// <summary>
    /// Gets the bit at the specified index from the <see cref="Value"/> property.
    /// </summary>
    /// <param name="bit">the index</param>
    /// <returns>The bit at the specified index</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetBit(int bit) {
        return (Value & 1 << bit) != 0;
    }

    /// <summary>
    /// Sets the bit at the specified index to the specified value.
    /// </summary>
    /// <param name="bit">The index</param>
    /// <param name="value">The value, interpreted as 0 or 1.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBit(int bit, bool value) {
        int mask = 1 << bit;
        Value = (byte)(Value & ~mask | (value ? mask : 0x00));
    }

    /// <inheritdoc/>
    public override string ToString() {
        return Convert.ToString(Value, 2).PadLeft(8, '0');
    }
}