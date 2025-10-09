namespace Spice86.Core.Emulator.CPU;

using Spice86.Shared.Utils;

using System.Collections.Frozen;
using System.Text;

/// <summary> Handles the CPU flag register. </summary>
public class Flags {
    private static readonly IDictionary<CpuModel, BitsOnOff> BitOnOffPerModel = new Dictionary<CpuModel, BitsOnOff> {
        // This is for the zet project tests
        [CpuModel.ZET_86] = new(bitsAlwaysOn: [1], bitsAlwaysOff: [3, 5, 15]),
        // Real CPUs
        [CpuModel.INTEL_8086] = new(bitsAlwaysOn: [1, 12, 13, 14, 15], bitsAlwaysOff: [3, 5]),
        // Since we dont handle IO privilege (12 / 13) and nested task (14), let's put them as always off
        [CpuModel.INTEL_80286] = new(bitsAlwaysOn: [1], bitsAlwaysOff: [3, 5, 12, 13, 14, 15]),
        [CpuModel.INTEL_80386] = new(bitsAlwaysOn: [1], bitsAlwaysOff: [3, 5, 12, 13, 14, 15])
    }.ToFrozenDictionary();
    
    private record BitsOnOff {
        public BitsOnOff(List<int> bitsAlwaysOn, List<int> bitsAlwaysOff) {
            BitsAlwaysOn = BitMaskUtils.BitMaskFromBitList(bitsAlwaysOn);
            BitsAlwaysOff = ~BitMaskUtils.BitMaskFromBitList(bitsAlwaysOff);
        }
        /// <summary>
        /// Or that into the register
        /// </summary>
        public uint BitsAlwaysOn { get; }
        
        /// <summary>
        /// And that into the register
        /// </summary>
        public uint BitsAlwaysOff { get; }
    }
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
    private uint _orFlagMask;
    private uint _andFlagMask;

    private uint _flagRegister;

    private CpuModel _cpuModel;
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public Flags(CpuModel cpuModel) {
        CpuModel = cpuModel;
        FlagRegister = 0;
    }

    public CpuModel CpuModel {
        get => _cpuModel;
        set {
            _cpuModel = value;
            BitsOnOff bitsOnOff = BitOnOffPerModel[_cpuModel];
            _orFlagMask = bitsOnOff.BitsAlwaysOn;
            _andFlagMask = bitsOnOff.BitsAlwaysOff;
        }
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
            _flagRegister |= mask;
        } else {
            _flagRegister &= (ushort)~mask;
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
        set => _flagRegister = (value | _orFlagMask) & _andFlagMask;
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