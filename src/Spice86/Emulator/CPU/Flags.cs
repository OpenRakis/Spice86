namespace Spice86.Emulator.CPU;

using Spice86.Utils;

using System.Text;

/// <summary> Handles the CPU flag register. </summary>
public class Flags {
    public const ushort Auxiliary = 0b00000000_00010000;

    public const ushort Carry = 0b00000000_00000001;

    public const ushort Direction = 0b00000100_00000000;

    public const ushort Interrupt = 0b00000010_00000000;

    public const ushort Overflow = 0b00001000_00000000;

    public const ushort Parity = 0b00000000_00000100;

    public const ushort Sign = 0b00000000_10000000;

    public const ushort Trap = 0b00000001_00000000;

    public const ushort Zero = 0b00000000_01000000;

    // rflag mask to OR with flags, useful to compare values with dosbox which emulates
    private ushort _additionalFlagMask;

    private ushort _flagRegister;

    public Flags() {
        this.SetFlagRegister(0);
    }

    public static string DumpFlags(int flags) {
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

    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not Flags other) {
            return false;
        }
        return _flagRegister == other._flagRegister;
    }

    public bool GetFlag(ushort mask) {
        return (_flagRegister & mask) == mask;
    }

    public ushort GetFlagRegister() {
        return _flagRegister;
    }

    public override int GetHashCode() {
        return _flagRegister;
    }

    public void SetDosboxCompatibility(bool compatible) {
        if (compatible) {
            _additionalFlagMask = 0b111000000000000;
        } else {
            _additionalFlagMask = 0;
        }
    }

    public void SetFlag(ushort mask, bool value) {
        if (value) {
            _flagRegister |= mask;
        } else {
            _flagRegister &= (ushort)~mask;
        }
    }

    public void SetFlagRegister(ushort value) {
        // Some flags are always 1 or 0 no matter what (8086)
        ushort modifedValue = (ushort)((value | 0b10) & 0b0111111111010111);

        // dosbox
        modifedValue |= _additionalFlagMask;
        _flagRegister = ConvertUtils.Uint16(modifedValue);
    }

    public override string ToString() {
        return DumpFlags(_flagRegister);
    }

    private static char GetFlag(int flags, int mask, char representation) {
        if ((flags & mask) == 0) {
            return ' ';
        }
        return representation;
    }
}