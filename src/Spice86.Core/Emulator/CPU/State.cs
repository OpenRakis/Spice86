namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.CPU.Registers;

using System.Text;

using Spice86.Shared.Utils;

/// <summary>
/// Represents the state of the CPU Registers and Flags.
/// </summary>
public class State : IDebuggableComponent {
    // Accumulator
    public byte AH { get => GeneralRegisters.UInt8High[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.AxIndex] = value; }
    public byte AL { get => GeneralRegisters.UInt8Low[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.AxIndex] = value; }
    public ushort AX { get => GeneralRegisters.UInt16[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.AxIndex] = value; }
    public uint EAX { get => GeneralRegisters.UInt32[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.AxIndex] = value; }

    // Base
    public byte BH { get => GeneralRegisters.UInt8High[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.BxIndex] = value; }
    public byte BL { get => GeneralRegisters.UInt8Low[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.BxIndex] = value; }
    public ushort BX { get => GeneralRegisters.UInt16[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.BxIndex] = value; }
    public uint EBX { get => GeneralRegisters.UInt32[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.BxIndex] = value; }

    // Counter
    public byte CH { get => GeneralRegisters.UInt8High[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.CxIndex] = value; }
    public byte CL { get => GeneralRegisters.UInt8Low[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.CxIndex] = value; }
    public ushort CX { get => GeneralRegisters.UInt16[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.CxIndex] = value; }
    public uint ECX { get => GeneralRegisters.UInt32[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.CxIndex] = value; }

    // Data
    public byte DH { get => GeneralRegisters.UInt8High[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.DxIndex] = value; }
    public byte DL { get => GeneralRegisters.UInt8Low[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.DxIndex] = value; }
    public ushort DX { get => GeneralRegisters.UInt16[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.DxIndex] = value; }
    public uint EDX { get => GeneralRegisters.UInt32[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.DxIndex] = value; }

    // Destination Index
    public ushort DI { get => GeneralRegisters.UInt16[GeneralRegisters.DiIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.DiIndex] = value; }
    public uint EDI { get => GeneralRegisters.UInt32[GeneralRegisters.DiIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.DiIndex] = value; }

    // Source Index
    public ushort SI { get => GeneralRegisters.UInt16[GeneralRegisters.SiIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.SiIndex] = value; }
    public uint ESI { get => GeneralRegisters.UInt32[GeneralRegisters.SiIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.SiIndex] = value; }

    // Base Pointer
    public ushort BP { get => GeneralRegisters.UInt16[GeneralRegisters.BpIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.BpIndex] = value; }
    public uint EBP { get => GeneralRegisters.UInt32[GeneralRegisters.BpIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.BpIndex] = value; }

    // Stack Pointer
    public ushort SP { get => GeneralRegisters.UInt16[GeneralRegisters.SpIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.SpIndex] = value; }
    public uint ESP { get => GeneralRegisters.UInt32[GeneralRegisters.SpIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.SpIndex] = value; }

    // Code Segment
    public ushort CS { get => SegmentRegisters.UInt16[SegmentRegisters.CsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.CsIndex] = value; }

    // Data Segment
    public ushort DS { get => SegmentRegisters.UInt16[SegmentRegisters.DsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.DsIndex] = value; }

    // Extra segments
    public ushort ES { get => SegmentRegisters.UInt16[SegmentRegisters.EsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.EsIndex] = value; }
    public ushort FS { get => SegmentRegisters.UInt16[SegmentRegisters.FsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.FsIndex] = value; }
    public ushort GS { get => SegmentRegisters.UInt16[SegmentRegisters.GsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.GsIndex] = value; }

    // Stack Segment
    public ushort SS { get => SegmentRegisters.UInt16[SegmentRegisters.SsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.SsIndex] = value; }

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
    public uint? SegmentOverrideIndex { get; set; }

    /// <summary>
    /// The number of CPU cycles, incremented on each new instruction.
    /// </summary>
    public long Cycles { get; private set; }
    public uint IpPhysicalAddress => MemoryUtils.ToPhysicalAddress(CS, IP);
    public uint StackPhysicalAddress => MemoryUtils.ToPhysicalAddress(SS, SP);

    public GeneralRegisters GeneralRegisters { get; } = new();
    public SegmentRegisters SegmentRegisters { get; } = new();

    public bool IsRunning { get; set; } = true;

    /// <summary>
    /// Sets <see cref="ContinueZeroFlagValue"/> and <see cref="SegmentOverrideIndex"/> to <c>null</c>.
    /// </summary>
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
            StringBuilder res = new();
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

    /// <inheritdoc/>
    public void Accept(IEmulatorDebugger emulatorDebugger) {
        emulatorDebugger.VisitCpuState(this);
    }
}