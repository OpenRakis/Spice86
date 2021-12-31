namespace Ix86.Emulator.CPU;

using Ix86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Handles the CPU flag register.
/// </summary>
public class Flags
{
    public const int Carry = 0b00000000_00000001;
    public const int Parity = 0b00000000_00000100;
    public const int Auxiliary = 0b00000000_00010000;
    public const int Zero = 0b00000000_01000000;
    public const int Sign = 0b00000000_10000000;
    public const int Trap = 0b00000001_00000000;
    public const int Interrupt = 0b00000010_00000000;
    public const int Direction = 0b00000100_00000000;
    public const int Overflow = 0b00001000_00000000;

    // rflag mask to OR with flags, useful to compare values with dosbox which emulates
    private int _additionalFlagMask;
    private int _flagRegister;

    private static char GetFlag(int flags, int mask, char representation)
    {
        if ((flags & mask) == 0)
        {
            return ' ';
        }
        return representation;
    }

    public static string DumpFlags(int flags)
    {
        var res = new StringBuilder();
        res.Append(GetFlag(flags, Flags.Overflow, 'O'));
        res.Append(GetFlag(flags, Flags.Direction, 'D'));
        res.Append(GetFlag(flags, Flags.Interrupt, 'I'));
        res.Append(GetFlag(flags, Flags.Trap, 'T'));
        res.Append(GetFlag(flags, Flags.Sign, 'S'));
        res.Append(GetFlag(flags, Flags.Zero, 'Z'));
        res.Append(GetFlag(flags, Flags.Auxiliary, 'A'));
        res.Append(GetFlag(flags, Flags.Parity, 'P'));
        res.Append(GetFlag(flags, Flags.Carry, 'C'));
        return res.ToString();
    }


    public Flags()
    {
        this.SetFlagRegister(0);
    }

    public void SetDosboxCompatibility(bool compatible)
    {
        if (compatible)
        {
            _additionalFlagMask = 0b111000000000000;
        }
        else
        {
            _additionalFlagMask = 0;
        }
    }

    public int GetFlagRegister()
    {
        return _flagRegister;
    }

    public void SetFlagRegister(int value)
    {
        // Some flags are always 1 or 0 no matter what (8086)
        int modifedValue = (value | 0b10) & 0b0111111111010111;
        // dosbox
        modifedValue |= _additionalFlagMask;
        _flagRegister = ConvertUtils.Uint16(modifedValue);
    }

    public bool GetFlag(int mask)
    {
        return (_flagRegister & mask) == mask;
    }

    public void SetFlag(int mask, bool value)
    {
        if (value)
        {
            _flagRegister |= mask;
        }
        else
        {
            _flagRegister &= ~mask;
        }
    }

    public override string ToString()
    {
        return DumpFlags(_flagRegister);
    }

    public override int GetHashCode()
    {
        return _flagRegister;
    }

    public override bool Equals(object? obj)
    {
        if(obj == this)
        {
            return true;
        }
        if(obj is not Flags other)
        {
            return false;
        }
        return _flagRegister == other._flagRegister;
    }
}
