namespace Spice86.Core.Emulator.CPU;

using System.Text;

/// <summary> Handles the CPU flag register. </summary>
public class Flags {
    public const ushort Carry = 0b00000000_00000001; //0
    public const ushort Parity = 0b00000000_00000100; //2
    public const ushort Auxiliary = 0b00000000_00010000; //4
    public const ushort Zero = 0b00000000_01000000; //6
    public const ushort Sign = 0b00000000_10000000; //7
    public const ushort Trap = 0b00000001_00000000; //8
    public const ushort Interrupt = 0b00000010_00000000; //9
    public const ushort Direction = 0b00000100_00000000; //10
    public const ushort Overflow = 0b00001000_00000000; //11

    // rflag mask to OR with flags, useful to compare values with dosbox which emulates
    private ushort _additionalFlagMask;
    private uint _flagRegister;
    public Flags() {
        FlagRegister = 0;
    }

    public bool GetFlag(ushort mask) {
        return (FlagRegister & mask) == mask;
    }

    public void SetFlag(ushort mask, bool value) {
        if (value) {
            FlagRegister |= mask;
        } else {
            FlagRegister &= (ushort)~mask;
        }
    }

    public bool IsDOSBoxCompatible {
        get => _additionalFlagMask == 0b111000000000000;
        set {
            if (value) { _additionalFlagMask = 0b111000000000000; } else { _additionalFlagMask = 0; }
        }
    }

    public ushort FlagRegister16 { get => (ushort)FlagRegister; }
    public uint FlagRegister {
        get => _flagRegister;
        set {
            // Some flags are always 1 or 0 no matter what (8086)
            uint modifedValue = ((value | 0b10) & 0b0111111111010111);

            // dosbox
            modifedValue |= _additionalFlagMask;
            _flagRegister = modifedValue;
        }
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

    public override int GetHashCode() {
        return (int)FlagRegister;
    }
    
    public override string ToString() {
        return DumpFlags(FlagRegister);
    }

    public static string DumpFlags(uint flags) {
        StringBuilder res = new StringBuilder();
        res.Append(GetFlag(flags, Overflow, 'O'));
        res.Append(GetFlag(flags, Direction, 'D'));
        res.Append(GetFlag(flags, Interrupt, 'I'));
        res.Append(GetFlag(flags, Trap, 'T'));
        res.Append(GetFlag(flags, Sign, 'S'));
        res.Append(GetFlag(flags, Zero, 'Z'));
        res.Append(GetFlag(flags, Auxiliary, 'A'));
        res.Append(GetFlag(flags, Parity, 'P'));
        res.Append(GetFlag(flags, Carry, 'C'));
        return res.ToString();
    }

    private static char GetFlag(uint flags, int mask, char representation) {
        if ((flags & mask) == 0) {
            return ' ';
        }
        return representation;
    }
}