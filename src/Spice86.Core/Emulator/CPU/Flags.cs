namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Debugger;

using System.Text;

/// <summary> Handles the CPU flag register. </summary>
public class Flags {
    /// <summary>
    /// The carry flag bitmask
    /// </summary>
    public const ushort Carry = 0b00000000_00000001; //0
    
    /// <summary>
    /// The parity flag bitmask
    /// </summary>
    public const ushort Parity = 0b00000000_00000100; //2
    
    /// <summary>
    /// The auxiliary flag bitmask
    /// </summary>
    public const ushort Auxiliary = 0b00000000_00010000; //4
    
    /// <summary>
    /// The zero flag bitmask
    /// </summary>
    public const ushort Zero = 0b00000000_01000000; //6
    
    /// <summary>
    /// The sign flag bitmask
    /// </summary>
    public const ushort Sign = 0b00000000_10000000; //7
    
    /// <summary>
    /// The trap flag bitmask
    /// </summary>
    public const ushort Trap = 0b00000001_00000000; //8
    
    /// <summary>
    /// The interrupt flag bitmask
    /// </summary>
    public const ushort Interrupt = 0b00000010_00000000; //9
    
    /// <summary>
    /// The direction flag bitmask
    /// </summary>
    public const ushort Direction = 0b00000100_00000000; //10
    
    /// <summary>
    /// The overflow flag bitmask
    /// </summary>
    public const ushort Overflow = 0b00001000_00000000; //11

    /// <summary>
    /// rflag mask to OR with flags, useful to compare with values emulated by DOSBox.
    /// </summary>
    private ushort _additionalFlagMask;
    
    private uint _flagRegister;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public Flags() {
        FlagRegister = 0;
        IsDOSBoxCompatible = true;
    }

    /// <summary>
    /// Gets the value of a particular flag
    /// </summary>
    /// <param name="mask">The bitmask to apply on the flags register.</param>
    /// <returns>The value of a particular flag, as a boolean.</returns>
    public bool GetFlag(ushort mask) {
        return (FlagRegister & mask) == mask;
    }

    /// <summary>
    /// Sets the value of a particular flag
    /// </summary>
    /// <param name="mask">The bitmask to access a particular flag in the flags register.</param>
    /// <param name="value">The boolean value of the flag.</param>
    public void SetFlag(ushort mask, bool value) {
        if (value) {
            FlagRegister |= mask;
        } else {
            FlagRegister &= (ushort)~mask;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the _additionalFlagMask is set to <c>0b111000000000000</c> or <c>0</c>. <br/>
    /// Useful when comparing with values emulated by DOSBox.
    /// <remarks>
    /// Set to <c>true</c> by default.
    /// </remarks>
    /// </summary>
    public bool IsDOSBoxCompatible {
        get => _additionalFlagMask == 0b111000000000000;
        set {
            if (value) { _additionalFlagMask = 0b111000000000000; } else { _additionalFlagMask = 0; }
        }
    }
    
    /// <summary>
    /// Gets the 16-bit value of the flags register.
    /// </summary>
    public ushort FlagRegister16 { get => (ushort)FlagRegister; }
    
    /// <summary>
    /// Gets the 32-bit value of the flags register.
    /// </summary>
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

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not Flags other) {
            return false;
        }
        return FlagRegister == other.FlagRegister;
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return (int)FlagRegister;
    }

    /// <inheritdoc />
    public override string ToString() {
        return DumpFlags(FlagRegister);
    }

    /// <summary>
    /// Returns a string representation of all flags in the flags register.
    /// </summary>
    /// <param name="flags">The value of the flags register.</param>
    /// <returns>A string representation of all flags in the flags register.</returns>
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