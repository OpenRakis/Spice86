namespace Spice86.Emulator.Cpu;

using Spice86.Emulator.Memory;
using Spice86.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class State
{
    private readonly Registers registers = new();
    private readonly SegmentRegisters segmentRegisters = new();
    private int ip;
    private readonly Flags flags = new();
    private long cycles;
    private int? segmentOverrideIndex = null;
    private bool? continueZeroFlagValue = null;
    private string currentInstructionPrefix = "";
    private string currentInstructionName = "";
    public virtual Registers GetRegisters()
    {
        return registers;
    }

    public virtual SegmentRegisters GetSegmentRegisters()
    {
        return segmentRegisters;
    }

    public virtual Flags GetFlags()
    {
        return flags;
    }

    public virtual int GetAX()
    {
        return registers.GetRegister(Registers.AxIndex);
    }

    public virtual void SetAX(int value)
    {
        registers.SetRegister(Registers.AxIndex, value);
    }

    public virtual int GetAL()
    {
        return registers.GetRegister8L(Registers.AxIndex);
    }

    public virtual void SetAL(int value)
    {
        registers.SetRegister8L(Registers.AxIndex, value);
    }

    public virtual int GetAH()
    {
        return registers.GetRegister8H(Registers.AxIndex);
    }

    public virtual void SetAH(int value)
    {
        registers.SetRegister8H(Registers.AxIndex, value);
    }

    public virtual int GetCX()
    {
        return registers.GetRegister(Registers.CxIndex);
    }

    public virtual void SetCX(int value)
    {
        registers.SetRegister(Registers.CxIndex, value);
    }

    public virtual int GetCL()
    {
        return registers.GetRegister8L(Registers.CxIndex);
    }

    public virtual void SetCL(int value)
    {
        registers.SetRegister8L(Registers.CxIndex, value);
    }

    public virtual int GetCH()
    {
        return registers.GetRegister8H(Registers.CxIndex);
    }

    public virtual void SetCH(int value)
    {
        registers.SetRegister8H(Registers.CxIndex, value);
    }

    public virtual int GetDX()
    {
        return registers.GetRegister(Registers.DxIndex);
    }

    public virtual void SetDX(int value)
    {
        registers.SetRegister(Registers.DxIndex, value);
    }

    public virtual int GetDL()
    {
        return registers.GetRegister8L(Registers.DxIndex);
    }

    public virtual void SetDL(int value)
    {
        registers.SetRegister8L(Registers.DxIndex, value);
    }

    public virtual int GetDH()
    {
        return registers.GetRegister8H(Registers.DxIndex);
    }

    public virtual void SetDH(int value)
    {
        registers.SetRegister8H(Registers.DxIndex, value);
    }

    public virtual int GetBX()
    {
        return registers.GetRegister(Registers.BxIndex);
    }

    public virtual void SetBX(int value)
    {
        registers.SetRegister(Registers.BxIndex, value);
    }

    public virtual int GetBL()
    {
        return registers.GetRegister8L(Registers.BxIndex);
    }

    public virtual void SetBL(int value)
    {
        registers.SetRegister8L(Registers.BxIndex, value);
    }

    public virtual int GetBH()
    {
        return registers.GetRegister8H(Registers.BxIndex);
    }

    public virtual void SetBH(int value)
    {
        registers.SetRegister8H(Registers.BxIndex, value);
    }

    public virtual int GetSP()
    {
        return registers.GetRegister(Registers.SpIndex);
    }

    public virtual void SetSP(int value)
    {
        registers.SetRegister(Registers.SpIndex, value);
    }

    public virtual int GetBP()
    {
        return registers.GetRegister(Registers.BpIndex);
    }

    public virtual void SetBP(int value)
    {
        registers.SetRegister(Registers.BpIndex, value);
    }

    public virtual int GetSI()
    {
        return registers.GetRegister(Registers.SiIndex);
    }

    public virtual void SetSI(int value)
    {
        registers.SetRegister(Registers.SiIndex, value);
    }

    public virtual int GetDI()
    {
        return registers.GetRegister(Registers.DiIndex);
    }

    public virtual void SetDI(int value)
    {
        registers.SetRegister(Registers.DiIndex, value);
    }

    public virtual int GetES()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.ES_INDEX);
    }

    public virtual void SetES(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.ES_INDEX, value);
    }

    public virtual int GetCS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.CS_INDEX);
    }

    public virtual void SetCS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.CS_INDEX, value);
    }

    public virtual int GetSS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.SS_INDEX);
    }

    public virtual void SetSS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.SS_INDEX, value);
    }

    public virtual int GetDS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.DS_INDEX);
    }

    public virtual void SetDS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.DS_INDEX, value);
    }

    public virtual int GetFS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.FS_INDEX);
    }

    public virtual void SetFS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.FS_INDEX, value);
    }

    public virtual int GetGS()
    {
        return segmentRegisters.GetRegister(SegmentRegisters.GS_INDEX);
    }

    public virtual void SetGS(int value)
    {
        segmentRegisters.SetRegister(SegmentRegisters.GS_INDEX, value);
    }

    public virtual int GetIP()
    {
        return ip;
    }

    public virtual void SetIP(int value)
    {
        ip = ConvertUtils.Uint16(value);
    }

    public virtual bool GetCarryFlag()
    {
        return flags.GetFlag(Flags.Carry);
    }

    public virtual bool GetParityFlag()
    {
        return flags.GetFlag(Flags.Parity);
    }

    public virtual bool GetAuxiliaryFlag()
    {
        return flags.GetFlag(Flags.Auxiliary);
    }

    public virtual bool GetZeroFlag()
    {
        return flags.GetFlag(Flags.Zero);
    }

    public virtual bool GetSignFlag()
    {
        return flags.GetFlag(Flags.Sign);
    }

    public virtual bool GetTrapFlag()
    {
        return flags.GetFlag(Flags.Trap);
    }

    public virtual bool GetInterruptFlag()
    {
        return flags.GetFlag(Flags.Interrupt);
    }

    public virtual bool GetDirectionFlag()
    {
        return flags.GetFlag(Flags.Direction);
    }

    public virtual bool GetOverflowFlag()
    {
        return flags.GetFlag(Flags.Overflow);
    }

    public virtual void SetCarryFlag(bool value)
    {
        flags.SetFlag(Flags.Carry, value);
    }

    public virtual void SetParityFlag(bool value)
    {
        flags.SetFlag(Flags.Parity, value);
    }

    public virtual void SetAuxiliaryFlag(bool value)
    {
        flags.SetFlag(Flags.Auxiliary, value);
    }

    public virtual void SetZeroFlag(bool value)
    {
        flags.SetFlag(Flags.Zero, value);
    }

    public virtual void SetSignFlag(bool value)
    {
        flags.SetFlag(Flags.Sign, value);
    }

    public virtual void SetTrapFlag(bool value)
    {
        flags.SetFlag(Flags.Trap, value);
    }

    public virtual void SetInterruptFlag(bool value)
    {
        flags.SetFlag(Flags.Interrupt, value);
    }

    public virtual void SetDirectionFlag(bool value)
    {
        flags.SetFlag(Flags.Direction, value);
    }

    public virtual void SetOverflowFlag(bool value)
    {
        flags.SetFlag(Flags.Overflow, value);
    }

    public virtual int? GetSegmentOverrideIndex()
    {
        return segmentOverrideIndex;
    }

    public virtual void SetSegmentOverrideIndex(int? segmentOverrideIndex)
    {
        this.segmentOverrideIndex = segmentOverrideIndex;
    }

    public virtual bool? GetContinueZeroFlagValue()
    {
        return continueZeroFlagValue;
    }

    public virtual void SetContinueZeroFlagValue(bool? continueZeroFlagValue)
    {
        this.continueZeroFlagValue = continueZeroFlagValue;
    }

    public virtual void ClearPrefixes()
    {
        this.SetContinueZeroFlagValue(null);
        this.SetSegmentOverrideIndex(null);
    }

    public virtual int GetStackPhysicalAddress()
    {
        return MemoryUtils.ToPhysicalAddress(this.GetSS(), this.GetSP());
    }

    public virtual int GetIpPhysicalAddress()
    {
        return MemoryUtils.ToPhysicalAddress(this.GetCS(), this.GetIP());
    }

    public virtual void ResetCurrentInstructionPrefix()
    {
        this.currentInstructionPrefix = "";
    }

    public virtual void AddCurrentInstructionPrefix(string currentInstructionPrefix)
    {
        this.currentInstructionPrefix += currentInstructionPrefix + " ";
    }

    public virtual string GetCurrentInstructionNameWithPrefix()
    {
        return currentInstructionPrefix + currentInstructionName;
    }

    public virtual void SetCurrentInstructionName(string currentInstructionName)
    {
        this.currentInstructionName = currentInstructionName;
    }

    public virtual long GetCycles()
    {
        return cycles;
    }

    public virtual void SetCycles(long cycles)
    {
        this.cycles = cycles;
    }

    public virtual void IncCycles()
    {
        cycles++;
    }

    public virtual string DumpRegFlags()
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

    public override string ToString()
    {
        return DumpRegFlags();
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ip, flags, registers, segmentRegisters);
    }
}