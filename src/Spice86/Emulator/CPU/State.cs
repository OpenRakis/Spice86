namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Text;

public class State {

    public void AddCurrentInstructionPrefix(string currentInstructionPrefix) {
        CurrentInstructionPrefix += $"{currentInstructionPrefix} ";
    }

    public void ClearPrefixes() {
        ContinueZeroFlagValue = null;
        SegmentOverrideIndex = null;
        OperandSizeOverride = false;
    }

    public string DumpedRegFlags {
        get {
            var res = new StringBuilder();
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
            res.Append(" flags=").Append(ConvertUtils.ToHex16(Flags.FlagRegister));
            res.Append(" (");
            res.Append(Flags);
            res.Append(')');
            return res.ToString();
        }
    }
    public string CurrentInstructionPrefix { get; private set; } = "";

    public byte AH { get => Registers.GetRegister8H(CPU.Registers.AxIndex); set => Registers.SetRegister8H(CPU.Registers.AxIndex, value); }

    public byte AL { get => Registers.GetRegister8L(CPU.Registers.AxIndex); set => Registers.SetRegister8L(CPU.Registers.AxIndex, value); }

    public bool AuxiliaryFlag { get => Flags.GetFlag(CPU.Flags.Auxiliary); set => Flags.SetFlag(CPU.Flags.Auxiliary, value); }

    public ushort AX { get => Registers.GetRegister(CPU.Registers.AxIndex); set => Registers.SetRegister(CPU.Registers.AxIndex, value); }

    public uint EAX { get => Registers.GetRegister32(CPU.Registers.AxIndex); set => Registers.SetRegister32(CPU.Registers.AxIndex, value); }

    public byte BH { get => Registers.GetRegister8H(CPU.Registers.BxIndex); set => Registers.SetRegister8H(CPU.Registers.BxIndex, value); }

    public byte BL { get => Registers.GetRegister8L(CPU.Registers.BxIndex); set => Registers.SetRegister8L(CPU.Registers.BxIndex, value); }

    public uint EBP { get => Registers.GetRegister32(CPU.Registers.BpIndex); set => Registers.SetRegister32(CPU.Registers.BpIndex, value); }

    public ushort BP { get => Registers.GetRegister(CPU.Registers.BpIndex); set => Registers.SetRegister(CPU.Registers.BpIndex, value); }

    public uint EBX { get => Registers.GetRegister32(CPU.Registers.BxIndex); set => Registers.SetRegister32(CPU.Registers.BxIndex, value); }

    public ushort BX { get => Registers.GetRegister(CPU.Registers.BxIndex); set => Registers.SetRegister(CPU.Registers.BxIndex, value); }

    public bool CarryFlag { get => Flags.GetFlag(CPU.Flags.Carry); set => Flags.SetFlag(CPU.Flags.Carry, value); }

    public byte CH { get => Registers.GetRegister8H(CPU.Registers.CxIndex); set => Registers.SetRegister8H(CPU.Registers.CxIndex, value); }

    public byte CL { get => Registers.GetRegister8L(CPU.Registers.CxIndex); set => Registers.SetRegister8L(CPU.Registers.CxIndex, value); }

    public bool? ContinueZeroFlagValue { get; set; }

    public ushort CS { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.CsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.CsIndex, value); }

    public string CurrentInstructionName { get; set; } = "";

    public string CurrentInstructionNameWithPrefix => $"{CurrentInstructionPrefix}{CurrentInstructionName}";

    public uint ECX { get => Registers.GetRegister32(CPU.Registers.CxIndex); set => Registers.SetRegister32(CPU.Registers.CxIndex, value); }

    public ushort CX { get => Registers.GetRegister(CPU.Registers.CxIndex); set => Registers.SetRegister(CPU.Registers.CxIndex, value); }

    public long Cycles { get; private set; }

    public byte DH { get => Registers.GetRegister8H(CPU.Registers.DxIndex); set => Registers.SetRegister8H(CPU.Registers.DxIndex, value); }

    public uint EDI { get => Registers.GetRegister32(CPU.Registers.DiIndex); set => Registers.SetRegister32(CPU.Registers.DiIndex, value); }

    public ushort DI { get => Registers.GetRegister(CPU.Registers.DiIndex); set => Registers.SetRegister(CPU.Registers.DiIndex, value); }

    public bool DirectionFlag { get => Flags.GetFlag(CPU.Flags.Direction); set => Flags.SetFlag(CPU.Flags.Direction, value); }

    public byte DL { get => Registers.GetRegister8L(CPU.Registers.DxIndex); set => Registers.SetRegister8L(CPU.Registers.DxIndex, value); }

    public ushort DS { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.DsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.DsIndex, value); }

    public uint EDX { get => Registers.GetRegister32(CPU.Registers.DxIndex); set => Registers.SetRegister32(CPU.Registers.DxIndex, value); }

    public ushort DX { get => Registers.GetRegister(CPU.Registers.DxIndex); set => Registers.SetRegister(CPU.Registers.DxIndex, value); }

    public ushort ES { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.EsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.EsIndex, value); }

    public Flags Flags { get; private set; } = new();

    public ushort FS { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.FsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.FsIndex, value); }

    public ushort GS { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.GsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.GsIndex, value); }

    public override int GetHashCode() {
        return HashCode.Combine(IP, Flags, Registers, SegmentRegisters);
    }

    public bool InterruptFlag { get => Flags.GetFlag(CPU.Flags.Interrupt); set => Flags.SetFlag(CPU.Flags.Interrupt, value); }

    public ushort IP { get; set; }

    public uint IpPhysicalAddress => MemoryUtils.ToPhysicalAddress(CS, IP);

    public bool OverflowFlag { get => Flags.GetFlag(CPU.Flags.Overflow); set => Flags.SetFlag(CPU.Flags.Overflow, value); }

    public bool ParityFlag { get => Flags.GetFlag(CPU.Flags.Parity); set => Flags.SetFlag(CPU.Flags.Parity, value); }

    public Registers Registers { get; private set; } = new();

    public int? SegmentOverrideIndex { get; set; }

    public bool OperandSizeOverride { get; set; }

    public SegmentRegisters SegmentRegisters { get; private set; } = new();

    public uint ESI { get => Registers.GetRegister32(CPU.Registers.SiIndex); set => Registers.SetRegister32(CPU.Registers.SiIndex, value); }

    public ushort SI { get => Registers.GetRegister(CPU.Registers.SiIndex); set => Registers.SetRegister(Registers.SiIndex, value); }

    public bool SignFlag { get => Flags.GetFlag(CPU.Flags.Sign); set => Flags.SetFlag(CPU.Flags.Sign, value); }

    public uint ESP { get => Registers.GetRegister32(CPU.Registers.SpIndex); set => Registers.SetRegister32(CPU.Registers.SpIndex, value); }

    public ushort SP { get => Registers.GetRegister(CPU.Registers.SpIndex); set => Registers.SetRegister(CPU.Registers.SpIndex, value); }

    public ushort SS { get => SegmentRegisters.GetRegister(CPU.SegmentRegisters.SsIndex); set => SegmentRegisters.SetRegister(CPU.SegmentRegisters.SsIndex, value); }

    public uint StackPhysicalAddress => MemoryUtils.ToPhysicalAddress(SS, SP);

    public bool TrapFlag { get => Flags.GetFlag(CPU.Flags.Trap); set => Flags.SetFlag(CPU.Flags.Trap, value); }

    public bool ZeroFlag { get => Flags.GetFlag(CPU.Flags.Zero); set => Flags.SetFlag(CPU.Flags.Zero, value); }

    public void IncCycles() {
        Cycles++;
    }

    public void ResetCurrentInstructionPrefix() {
        CurrentInstructionPrefix = "";
    }

    public override string ToString() {
        return DumpedRegFlags;
    }
}
