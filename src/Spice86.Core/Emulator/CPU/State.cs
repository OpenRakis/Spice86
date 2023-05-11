namespace Spice86.Core.Emulator.CPU;

using System.Text;

using Spice86.Shared.Utils;

/// <summary>
/// Represents the state of the CPU
/// </summary>
public class State {
    // Accumulator
    public byte AH { get => Registers.GetRegister8H(Registers.AxIndex); set => Registers.SetRegister8H(Registers.AxIndex, value); }
    public byte AL { get => Registers.GetRegister8L(Registers.AxIndex); set => Registers.SetRegister8L(Registers.AxIndex, value); }
    public ushort AX { get => Registers.GetRegister16(Registers.AxIndex); set => Registers.SetRegister16(Registers.AxIndex, value); }
    public uint EAX { get => Registers.GetRegister32(Registers.AxIndex); set => Registers.SetRegister32(Registers.AxIndex, value); }

    // Base
    public byte BH { get => Registers.GetRegister8H(Registers.BxIndex); set => Registers.SetRegister8H(Registers.BxIndex, value); }
    public byte BL { get => Registers.GetRegister8L(Registers.BxIndex); set => Registers.SetRegister8L(Registers.BxIndex, value); }
    public ushort BX { get => Registers.GetRegister16(Registers.BxIndex); set => Registers.SetRegister16(Registers.BxIndex, value); }
    public uint EBX { get => Registers.GetRegister32(Registers.BxIndex); set => Registers.SetRegister32(Registers.BxIndex, value); }

    // Counter
    public byte CH { get => Registers.GetRegister8H(Registers.CxIndex); set => Registers.SetRegister8H(Registers.CxIndex, value); }
    public byte CL { get => Registers.GetRegister8L(Registers.CxIndex); set => Registers.SetRegister8L(Registers.CxIndex, value); }
    public ushort CX { get => Registers.GetRegister16(Registers.CxIndex); set => Registers.SetRegister16(Registers.CxIndex, value); }
    public uint ECX { get => Registers.GetRegister32(Registers.CxIndex); set => Registers.SetRegister32(Registers.CxIndex, value); }

    // Data
    public byte DH { get => Registers.GetRegister8H(Registers.DxIndex); set => Registers.SetRegister8H(Registers.DxIndex, value); }
    public byte DL { get => Registers.GetRegister8L(Registers.DxIndex); set => Registers.SetRegister8L(Registers.DxIndex, value); }
    public ushort DX { get => Registers.GetRegister16(Registers.DxIndex); set => Registers.SetRegister16(Registers.DxIndex, value); }
    public uint EDX { get => Registers.GetRegister32(Registers.DxIndex); set => Registers.SetRegister32(Registers.DxIndex, value); }

    // Destination Index
    public ushort DI { get => Registers.GetRegister16(Registers.DiIndex); set => Registers.SetRegister16(Registers.DiIndex, value); }
    public uint EDI { get => Registers.GetRegister32(Registers.DiIndex); set => Registers.SetRegister32(Registers.DiIndex, value); }

    // Source Index
    public ushort SI { get => Registers.GetRegister16(Registers.SiIndex); set => Registers.SetRegister16(Registers.SiIndex, value); }
    public uint ESI { get => Registers.GetRegister32(Registers.SiIndex); set => Registers.SetRegister32(Registers.SiIndex, value); }

    // Base Pointer
    public ushort BP { get => Registers.GetRegister16(Registers.BpIndex); set => Registers.SetRegister16(Registers.BpIndex, value); }
    public uint EBP { get => Registers.GetRegister32(Registers.BpIndex); set => Registers.SetRegister32(Registers.BpIndex, value); }

    // Stack Pointer
    public ushort SP { get => Registers.GetRegister16(Registers.SpIndex); set => Registers.SetRegister16(Registers.SpIndex, value); }
    public uint ESP { get => Registers.GetRegister32(Registers.SpIndex); set => Registers.SetRegister32(Registers.SpIndex, value); }

    // Code Segment
    public ushort CS { get => SegmentRegisters.GetRegister16(SegmentRegisters.CsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.CsIndex, value); }

    // Data Segment
    public ushort DS { get => SegmentRegisters.GetRegister16(SegmentRegisters.DsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.DsIndex, value); }

    // Extra segments
    public ushort ES { get => SegmentRegisters.GetRegister16(SegmentRegisters.EsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.EsIndex, value); }
    public ushort FS { get => SegmentRegisters.GetRegister16(SegmentRegisters.FsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.FsIndex, value); }
    public ushort GS { get => SegmentRegisters.GetRegister16(SegmentRegisters.GsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.GsIndex, value); }

    // Stack Segment
    public ushort SS { get => SegmentRegisters.GetRegister16(SegmentRegisters.SsIndex); set => SegmentRegisters.SetRegister16(SegmentRegisters.SsIndex, value); }

    /// <summary> Instruction pointer </summary>
    public ushort IP { get; set; }

    /// <summary>
    /// Flags register
    /// </summary>
    public Flags Flags { get; } = new();

    public bool OverflowFlag { get => Flags.GetFlag(Flags.Overflow); set => Flags.SetFlag(Flags.Overflow, value); }
    public bool DirectionFlag { get => Flags.GetFlag(Flags.Direction); set => Flags.SetFlag(Flags.Direction, value); }
    public bool InterruptFlag { get => Flags.GetFlag(Flags.Interrupt); set => Flags.SetFlag(Flags.Interrupt, value); }
    public bool TrapFlag { get => Flags.GetFlag(Flags.Trap); set => Flags.SetFlag(Flags.Trap, value); }
    public bool SignFlag { get => Flags.GetFlag(Flags.Sign); set => Flags.SetFlag(Flags.Sign, value); }
    public bool ZeroFlag { get => Flags.GetFlag(Flags.Zero); set => Flags.SetFlag(Flags.Zero, value); }
    public bool AuxiliaryFlag { get => Flags.GetFlag(Flags.Auxiliary); set => Flags.SetFlag(Flags.Auxiliary, value); }
    public bool ParityFlag { get => Flags.GetFlag(Flags.Parity); set => Flags.SetFlag(Flags.Parity, value); }
    public bool CarryFlag { get => Flags.GetFlag(Flags.Carry); set => Flags.SetFlag(Flags.Carry, value); }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 8 bit CPU instructions.
    /// </summary>

    public short Direction8 => (short)(DirectionFlag ? -1 : 1);
    
    /// <summary>
    /// Gets the offset value of the Direction Flag for 16 bit CPU instructions.
    /// </summary>

    public short Direction16 => (short)(DirectionFlag ? -2 : 2);
    
    /// <summary>
    /// Gets the offset value of the Direction Flag for 32 bit CPU instructions.
    /// </summary>
    public short Direction32 => (short)(DirectionFlag ? -4 : 4);

    public bool? ContinueZeroFlagValue { get; set; }
    public int? SegmentOverrideIndex { get; set; }

    /// <summary>
    /// The number of CPU cycles, incremented on each new instruction.
    /// </summary>
    public long Cycles { get; private set; }
    public uint IpPhysicalAddress => MemoryUtils.ToPhysicalAddress(CS, IP);
    public uint StackPhysicalAddress => MemoryUtils.ToPhysicalAddress(SS, SP);

    public Registers Registers { get; } = new();
    public SegmentRegisters SegmentRegisters { get; } = new();
    
    public void ClearPrefixes() {
        ContinueZeroFlagValue = null;
        SegmentOverrideIndex = null;
    }

    /// <summary>
    /// Increments the <see cref="Cycles"/> count.
    /// </summary>
    public void IncCycles() {
        Cycles++;
    }

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    public string DumpedRegFlags {
        get {
            StringBuilder res = new StringBuilder();
            res.Append(nameof(Cycles)).Append('=');
            res.Append(Cycles);
            res.Append(" CS:IP=").Append(ConvertUtils.ToSegmentedAddressRepresentation(CS, IP)).Append('/').Append(ConvertUtils.ToHex(MemoryUtils.ToPhysicalAddress(CS, IP)));
            res.Append(" EAX=").Append(ConvertUtils.ToHex32(EAX));
            res.Append(" EBX=").Append(ConvertUtils.ToHex32(EBX));
            res.Append(" ECX=").Append(ConvertUtils.ToHex32(ECX));
            res.Append(" EDX=").Append(ConvertUtils.ToHex32(EDX));
            res.Append(" ESI=").Append(ConvertUtils.ToHex32(ESI));
            res.Append(" EDI=").Append(ConvertUtils.ToHex32(EDI));
            res.Append(" EBP=").Append(ConvertUtils.ToHex32(EBP));
            res.Append(" ESP=").Append(ConvertUtils.ToHex32(ESP));
            res.Append(" SS=").Append(ConvertUtils.ToHex16(SS));
            res.Append(" DS=").Append(ConvertUtils.ToHex16(DS));
            res.Append(" ES=").Append(ConvertUtils.ToHex16(ES));
            res.Append(" FS=").Append(ConvertUtils.ToHex16(FS));
            res.Append(" GS=").Append(ConvertUtils.ToHex16(GS));
            res.Append(" flags=").Append(ConvertUtils.ToHex32(Flags.FlagRegister));
            res.Append(" (");
            res.Append(Flags);
            res.Append(')');
            return res.ToString();
        }
    }

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    public override string ToString() {
        return DumpedRegFlags;
    }
}