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
        this.FlagRegister = 0;
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
        return FlagRegister == other.FlagRegister;
    }

    public bool GetFlag(ushort mask) {
        return (FlagRegister & mask) == mask;
    }

    public ushort FlagRegister {
        get => _flagRegister;
        set {
            // Some flags are always 1 or 0 no matter what (8086)
            ushort modifedValue = (ushort)((value | 0b10) & 0b0111111111010111);

            // dosbox
            modifedValue |= _additionalFlagMask;
            _flagRegister = ConvertUtils.Uint16(modifedValue);
        }
    }

    public override int GetHashCode() {
        return FlagRegister;
    }

    public bool IsDOSBoxCompatible { get => _additionalFlagMask == 0b111000000000000; set { if (value) { _additionalFlagMask = 0b111000000000000; } else { _additionalFlagMask = 0; } } }

    public void SetFlag(ushort mask, bool value) {
        if (value) {
            FlagRegister |= mask;
        } else {
            FlagRegister &= (ushort)~mask;
        }
    }

    public override string ToString() {
        return DumpFlags(FlagRegister);
    }

    private static char GetFlag(int flags, int mask, char representation) {
        if ((flags & mask) == 0) {
            return ' ';
        }
        return representation;
    }
}