namespace Spice86.Emulator.Cpu;

using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;

public class State
{
    private readonly Flags flags = new();

    private readonly Registers registers = new();

    private readonly SegmentRegisters segmentRegisters = new();

    private bool? continueZeroFlagValue = null;

    private string currentInstructionName = "";

    private string currentInstructionPrefix = "";

    private long cycles;

    private int ip;

    private int? segmentOverrideIndex = null;

    public void AddCurrentInstructionPrefix(string currentInstructionPrefix)
    {
        this.currentInstructionPrefix += currentInstructionPrefix + " ";
    }

    public void ClearPrefixes()
    {
        this.SetContinueZeroFlagValue(null);
        this.SetSegmentOverrideIndex(null);
    }

    public string DumpRegFlags()
    {
        string res = "cycles=" + this.GetCycles();
        res += " CS:IP=" + ConvertUtils.ToSegmentedAddressRepresentation(GetCS(), GetIP()) + '/' + ConvertUtils.ToAbsoluteSegmentedAddress(GetCS(), GetIP());
        res += " AX=" + ConvertUtils.ToHex16(GetAX());
        res += " BX=" + ConvertUtils.ToHex16(GetBX());
        res += " CX=" + ConvertUtils.ToHex16(GetCX());
        res += " DX=" + ConvertUtils.ToHex16(GetDX());
        res += " SI=" + ConvertUtils.ToHex16(GetSI());
        res += " DI=" + ConvertUtils.ToHex16(GetDI());
        res += " BP=" + ConvertUtils.ToHex16(GetBP());
        res += " SP=" + ConvertUtils.ToHex16(GetSP());
        res += " SS=" + ConvertUtils.ToHex16(GetSS());
        res += " DS=" + ConvertUtils.ToHex16(GetDS());
        res += " ES=" + ConvertUtils.ToHex16(GetES());
        res += " FS=" + ConvertUtils.ToHex16(GetFS());
        res += " GS=" + ConvertUtils.ToHex16(GetGS());
        res += " flags=" + ConvertUtils.ToHex16(flags.GetFlagRegister());
        res += " (" + flags.ToString() + ")";
        return res;
    }

    public int GetAH()
    {
        return registers.GetRegister8H(Registers.AxIndex);
    }

    public int GetAL()
    {
        return registers.GetRegister8L(Registers.AxIndex);
    }

    public bool GetAuxiliaryFlag()
    {
        return flags.GetFlag(Flags.Auxiliary);
    }

    public int GetAX()
    {
        return registers.GetRegister(Registers.AxIndex);
    }

    public int GetBH()
    {
        return registers.GetRegister8H(Registers.BxIndex);
    }

    public int GetBL()
    {
        return registers.GetRegister8L(Registers.BxIndex);
    }

    public int GetBP()
    {
        return registers.GetRegister(Registers.BpIndex);
    }

    public int GetBX()
    {
        return registers.GetRegister(Registers.BxIndex);
    }

    public bool GetCarryFlag()
    {
        return flags.GetFlag(Flags.Carry);
    }

    public int GetCH()
    {
        return registers.GetRegister8H(Registers.CxIndex);
    }

    public int GetCL()
    {
        return registers.GetRegister8L(Registers.CxIndex);
    }

    public bool? GetContinueZeroFlagValue()
    {
        return continueZeroFlagValue;
    }

    public int GetCS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.CsIndex);
    }

    public string GetCurrentInstructionNameWithPrefix()
    {
        return currentInstructionPrefix + currentInstructionName;
    }

    public int GetCX()
    {
        return registers.GetRegister(Registers.CxIndex);
    }

    public long GetCycles()
    {
        return cycles;
    }

    public int GetDH()
    {
        return registers.GetRegister8H(Registers.DxIndex);
    }

    public int GetDI()
    {
        return registers.GetRegister(Registers.DiIndex);
    }

    public bool GetDirectionFlag()
    {
        return flags.GetFlag(Flags.Direction);
    }

    public int GetDL()
    {
        return registers.GetRegister8L(Registers.DxIndex);
    }

    public int GetDS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.DsIndex);
    }

    public int GetDX()
    {
        return registers.GetRegister(Registers.DxIndex);
    }

    public int GetES()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.EsIndex);
    }

    public Flags GetFlags()
    {
        return flags;
    }

    public int GetFS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.FsIndex);
    }

    public int GetGS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.GsIndex);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ip, flags, registers, segmentRegisters);
    }

    public bool GetInterruptFlag()
    {
        return flags.GetFlag(Flags.Interrupt);
    }

    public int GetIP()
    {
        return ip;
    }

    public int GetIpPhysicalAddress()
    {
        return MemoryUtils.ToPhysicalAddress(this.GetCS(), this.GetIP());
    }

    public bool GetOverflowFlag()
    {
        return flags.GetFlag(Flags.Overflow);
    }

    public bool GetParityFlag()
    {
        return flags.GetFlag(Flags.Parity);
    }

    public Registers GetRegisters()
    {
        return registers;
    }

    public int? GetSegmentOverrideIndex()
    {
        return segmentOverrideIndex;
    }

    public SegmentRegisters GetSegmentRegisters()
    {
        return segmentRegisters;
    }

    public int GetSI()
    {
        return registers.GetRegister(Registers.SiIndex);
    }

    public bool GetSignFlag()
    {
        return flags.GetFlag(Flags.Sign);
    }

    public int GetSP()
    {
        return registers.GetRegister(Registers.SpIndex);
    }

    public int GetSS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.SsIndex);
    }

    public int GetStackPhysicalAddress()
    {
        return MemoryUtils.ToPhysicalAddress(this.GetSS(), this.GetSP());
    }

    public bool GetTrapFlag()
    {
        return flags.GetFlag(Flags.Trap);
    }

    public bool GetZeroFlag()
    {
        return flags.GetFlag(Flags.Zero);
    }

    public void IncCycles()
    {
        cycles++;
    }

    public void ResetCurrentInstructionPrefix()
    {
        this.currentInstructionPrefix = "";
    }

    public void SetAH(int value)
    {
        registers.SetRegister8H(Registers.AxIndex, value);
    }

    public void SetAL(int value)
    {
        registers.SetRegister8L(Registers.AxIndex, value);
    }

    public void SetAuxiliaryFlag(bool value)
    {
        flags.SetFlag(Flags.Auxiliary, value);
    }

    public void SetAX(int value)
    {
        registers.SetRegister(Registers.AxIndex, value);
    }

    public void SetBH(int value)
    {
        registers.SetRegister8H(Registers.BxIndex, value);
    }

    public void SetBL(int value)
    {
        registers.SetRegister8L(Registers.BxIndex, value);
    }

    public void SetBP(int value)
    {
        registers.SetRegister(Registers.BpIndex, value);
    }

    public void SetBX(int value)
    {
        registers.SetRegister(Registers.BxIndex, value);
    }

    public void SetCarryFlag(bool value)
    {
        flags.SetFlag(Flags.Carry, value);
    }

    public void SetCH(int value)
    {
        registers.SetRegister8H(Registers.CxIndex, value);
    }

    public void SetCL(int value)
    {
        registers.SetRegister8L(Registers.CxIndex, value);
    }

    public void SetContinueZeroFlagValue(bool? continueZeroFlagValue)
    {
        this.continueZeroFlagValue = continueZeroFlagValue;
    }

    public void SetCS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.CsIndex, value);
    }

    public void SetCurrentInstructionName(string currentInstructionName)
    {
        this.currentInstructionName = currentInstructionName;
    }

    public void SetCX(int value)
    {
        registers.SetRegister(Registers.CxIndex, value);
    }

    public void SetCycles(long cycles)
    {
        this.cycles = cycles;
    }

    public void SetDH(int value)
    {
        registers.SetRegister8H(Registers.DxIndex, value);
    }

    public void SetDI(int value)
    {
        registers.SetRegister(Registers.DiIndex, value);
    }

    public void SetDirectionFlag(bool value)
    {
        flags.SetFlag(Flags.Direction, value);
    }

    public void SetDL(int value)
    {
        registers.SetRegister8L(Registers.DxIndex, value);
    }

    public void SetDS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.DsIndex, value);
    }

    public void SetDX(int value)
    {
        registers.SetRegister(Registers.DxIndex, value);
    }

    public void SetES(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.EsIndex, value);
    }

    public void SetFS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.FsIndex, value);
    }

    public void SetGS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.GsIndex, value);
    }

    public void SetInterruptFlag(bool value)
    {
        flags.SetFlag(Flags.Interrupt, value);
    }

    public void SetIP(int value)
    {
        ip = ConvertUtils.Uint16(value);
    }

    public void SetOverflowFlag(bool value)
    {
        flags.SetFlag(Flags.Overflow, value);
    }

    public void SetParityFlag(bool value)
    {
        flags.SetFlag(Flags.Parity, value);
    }

    public void SetSegmentOverrideIndex(int? segmentOverrideIndex)
    {
        this.segmentOverrideIndex = segmentOverrideIndex;
    }

    public void SetSI(int value)
    {
        registers.SetRegister(Registers.SiIndex, value);
    }

    public void SetSignFlag(bool value)
    {
        flags.SetFlag(Flags.Sign, value);
    }

    public void SetSP(int value)
    {
        registers.SetRegister(Registers.SpIndex, value);
    }

    public void SetSS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.SsIndex, value);
    }

    public void SetTrapFlag(bool value)
    {
        flags.SetFlag(Flags.Trap, value);
    }

    public void SetZeroFlag(bool value)
    {
        flags.SetFlag(Flags.Zero, value);
    }

    public override string ToString()
    {
        return DumpRegFlags();
    }
}