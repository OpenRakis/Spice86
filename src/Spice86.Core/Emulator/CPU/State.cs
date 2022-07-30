namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Utils;

using System.Text;

public class State {

    public void AddCurrentInstructionPrefix(string currentInstructionPrefix) {
        CurrentInstructionPrefix += $"{currentInstructionPrefix} ";
    }

    public void ClearPrefixes() {
        ContinueZeroFlagValue = null;
        SegmentOverrideIndex = null;
    }

    public string DumpedRegFlags {
        get {
            var res = new StringBuilder();
            res.Append(nameof(Cycles)).Append('=');
            res.Append(Cycles);
            res.Append(" CS:IP=").Append(ConvertUtils.ToSegmentedAddressRepresentation(CS, IP)).Append('/').Append(ConvertUtils.ToHex(MemoryUtils.ToPhysicalAddress(CS, IP)));
            res.Append(" AX=").Append(ConvertUtils.ToHex16(AX));
            res.Append(" BX=").Append(ConvertUtils.ToHex16(BX));
            res.Append(" CX=").Append(ConvertUtils.ToHex16(CX));
            res.Append(" DX=").Append(ConvertUtils.ToHex16(DX));
            res.Append(" SI=").Append(ConvertUtils.ToHex16(SI));
            res.Append(" DI=").Append(ConvertUtils.ToHex16(DI));
            res.Append(" BP=").Append(ConvertUtils.ToHex16(BP));
            res.Append(" SP=").Append(ConvertUtils.ToHex16(SP));
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

    public byte AH { get => Registers.GetRegister8H(Registers.AxIndex); set => Registers.SetRegister8H(Registers.AxIndex, value); }

    public byte AL { get => Registers.GetRegister8L(Registers.AxIndex); set => Registers.SetRegister8L(Registers.AxIndex, value); }

    public bool AuxiliaryFlag { get => Flags.GetFlag(Flags.Auxiliary); set => Flags.SetFlag(Flags.Auxiliary, value); }

    public ushort AX { get => Registers.GetRegister(Registers.AxIndex); set => Registers.SetRegister(Registers.AxIndex, value); }

    public byte BH { get => Registers.GetRegister8H(Registers.BxIndex); set => Registers.SetRegister8H(Registers.BxIndex, value); }

    public byte BL { get => Registers.GetRegister8L(Registers.BxIndex); set => Registers.SetRegister8L(Registers.BxIndex, value); }

    public ushort BP { get => Registers.GetRegister(Registers.BpIndex); set => Registers.SetRegister(Registers.BpIndex, value); }

    public ushort BX { get => Registers.GetRegister(Registers.BxIndex); set => Registers.SetRegister(Registers.BxIndex, value); }

    public bool CarryFlag { get => Flags.GetFlag(Flags.Carry); set => Flags.SetFlag(Flags.Carry, value); }

    public byte CH { get => Registers.GetRegister8H(Registers.CxIndex); set => Registers.SetRegister8H(Registers.CxIndex, value); }

    public byte CL { get => Registers.GetRegister8L(Registers.CxIndex); set => Registers.SetRegister8L(Registers.CxIndex, value); }

    public bool? ContinueZeroFlagValue { get; set; }

    public ushort CS { get => SegmentRegisters.GetRegister(SegmentRegisters.CsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.CsIndex, value); }

    public string CurrentInstructionName { get; set; } = "";

    public string CurrentInstructionNameWithPrefix => $"{CurrentInstructionPrefix}{CurrentInstructionName}";

    public ushort CX { get => Registers.GetRegister(Registers.CxIndex); set => Registers.SetRegister(Registers.CxIndex, value); }

    public long Cycles { get; private set; }

    public byte DH { get => Registers.GetRegister8H(Registers.DxIndex); set => Registers.SetRegister8H(Registers.DxIndex, value); }

    public ushort DI { get => Registers.GetRegister(Registers.DiIndex); set => Registers.SetRegister(Registers.DiIndex, value); }

    public bool DirectionFlag { get => Flags.GetFlag(Flags.Direction); set => Flags.SetFlag(Flags.Direction, value); }

    public byte DL { get => Registers.GetRegister8L(Registers.DxIndex); set => Registers.SetRegister8L(Registers.DxIndex, value); }

    public ushort DS { get => SegmentRegisters.GetRegister(SegmentRegisters.DsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.DsIndex, value); }

    public ushort DX { get => Registers.GetRegister(Registers.DxIndex); set => Registers.SetRegister(Registers.DxIndex, value); }

    public ushort ES { get => SegmentRegisters.GetRegister(SegmentRegisters.EsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.EsIndex, value); }

    public Flags Flags { get; private set; } = new();

    public ushort FS { get => SegmentRegisters.GetRegister(SegmentRegisters.FsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.FsIndex, value); }

    public ushort GS { get => SegmentRegisters.GetRegister(SegmentRegisters.GsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.GsIndex, value); }

    public bool InterruptFlag { get => Flags.GetFlag(Flags.Interrupt); set => Flags.SetFlag(Flags.Interrupt, value); }

    public ushort IP { get; set; }

    public uint IpPhysicalAddress => MemoryUtils.ToPhysicalAddress(CS, IP);

    public bool OverflowFlag { get => Flags.GetFlag(Flags.Overflow); set => Flags.SetFlag(Flags.Overflow, value); }

    public bool ParityFlag { get => Flags.GetFlag(Flags.Parity); set => Flags.SetFlag(Flags.Parity, value); }

    public Registers Registers { get; private set; } = new();

    public int? SegmentOverrideIndex { get; set; }

    public SegmentRegisters SegmentRegisters { get; private set; } = new();

    public ushort SI { get => Registers.GetRegister(Registers.SiIndex); set => Registers.SetRegister(Registers.SiIndex, value); }

    public bool SignFlag { get => Flags.GetFlag(Flags.Sign); set => Flags.SetFlag(Flags.Sign, value); }

    public ushort SP { get => Registers.GetRegister(Registers.SpIndex); set => Registers.SetRegister(Registers.SpIndex, value); }

    public ushort SS { get => SegmentRegisters.GetRegister(SegmentRegisters.SsIndex); set => SegmentRegisters.SetRegister(SegmentRegisters.SsIndex, value); }

    public uint StackPhysicalAddress => MemoryUtils.ToPhysicalAddress(SS, SP);

    public bool TrapFlag { get => Flags.GetFlag(Flags.Trap); set => Flags.SetFlag(Flags.Trap, value); }

    public bool ZeroFlag { get => Flags.GetFlag(Flags.Zero); set => Flags.SetFlag(Flags.Zero, value); }

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