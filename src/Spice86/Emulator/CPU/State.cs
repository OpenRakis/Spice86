namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;

public class State {
    private readonly Flags _flags = new();

    private readonly Registers _registers = new();

    private readonly SegmentRegisters _segmentRegisters = new();

    private bool? _continueZeroFlagValue = null;

    private string _currentInstructionName = "";

    private string _currentInstructionPrefix = "";

    private long _cycles;

    private ushort _ip;

    private int? _segmentOverrideIndex = null;

    public void AddCurrentInstructionPrefix(string currentInstructionPrefix) {
        this._currentInstructionPrefix += currentInstructionPrefix + " ";
    }

    public void ClearPrefixes() {
        this.SetContinueZeroFlagValue(null);
        this.SetSegmentOverrideIndex(null);
    }

    public string DumpRegFlags() {
        string res = "cycles=" + this.GetCycles();
        res += " CS:IP=" + ConvertUtils.ToSegmentedAddressRepresentation(GetCS(), GetIP()) + '/' + ConvertUtils.ToHex(MemoryUtils.ToPhysicalAddress(GetCS(), GetIP()));
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
        res += " flags=" + ConvertUtils.ToHex16(_flags.FlagRegister);
        res += " (" + _flags + ")";
        return res;
    }

    public byte GetAH() {
        return _registers.GetRegister8H(Registers.AxIndex);
    }

    public byte GetAL() {
        return _registers.GetRegister8L(Registers.AxIndex);
    }

    public bool GetAuxiliaryFlag() {
        return _flags.GetFlag(Flags.Auxiliary);
    }

    public ushort GetAX() {
        return _registers.GetRegister(Registers.AxIndex);
    }

    public byte GetBH() {
        return _registers.GetRegister8H(Registers.BxIndex);
    }

    public byte GetBL() {
        return _registers.GetRegister8L(Registers.BxIndex);
    }

    public ushort GetBP() {
        return _registers.GetRegister(Registers.BpIndex);
    }

    public ushort GetBX() {
        return _registers.GetRegister(Registers.BxIndex);
    }

    public bool GetCarryFlag() {
        return _flags.GetFlag(Flags.Carry);
    }

    public byte GetCH() {
        return _registers.GetRegister8H(Registers.CxIndex);
    }

    public byte GetCL() {
        return _registers.GetRegister8L(Registers.CxIndex);
    }

    public bool? GetContinueZeroFlagValue() {
        return _continueZeroFlagValue;
    }

    public ushort GetCS() {
        return _segmentRegisters.GetRegister(SegmentRegisters.CsIndex);
    }

    public string GetCurrentInstructionNameWithPrefix() {
        return _currentInstructionPrefix + _currentInstructionName;
    }

    public ushort GetCX() {
        return _registers.GetRegister(Registers.CxIndex);
    }

    public long GetCycles() {
        return _cycles;
    }

    public byte GetDH() {
        return _registers.GetRegister8H(Registers.DxIndex);
    }

    public ushort GetDI() {
        return _registers.GetRegister(Registers.DiIndex);
    }

    public bool GetDirectionFlag() {
        return _flags.GetFlag(Flags.Direction);
    }

    public byte GetDL() {
        return _registers.GetRegister8L(Registers.DxIndex);
    }

    public ushort GetDS() {
        return _segmentRegisters.GetRegister(SegmentRegisters.DsIndex);
    }

    public ushort GetDX() {
        return _registers.GetRegister(Registers.DxIndex);
    }

    public ushort GetES() {
        return _segmentRegisters.GetRegister(SegmentRegisters.EsIndex);
    }

    public Flags GetFlags() {
        return _flags;
    }

    public ushort GetFS() {
        return _segmentRegisters.GetRegister(SegmentRegisters.FsIndex);
    }

    public ushort GetGS() {
        return _segmentRegisters.GetRegister(SegmentRegisters.GsIndex);
    }

    public override int GetHashCode() {
        return HashCode.Combine(_ip, _flags, _registers, _segmentRegisters);
    }

    public bool GetInterruptFlag() {
        return _flags.GetFlag(Flags.Interrupt);
    }

    public ushort GetIP() {
        return _ip;
    }

    public uint GetIpPhysicalAddress() {
        return MemoryUtils.ToPhysicalAddress(this.GetCS(), this.GetIP());
    }

    public bool GetOverflowFlag() {
        return _flags.GetFlag(Flags.Overflow);
    }

    public bool GetParityFlag() {
        return _flags.GetFlag(Flags.Parity);
    }

    public Registers GetRegisters() {
        return _registers;
    }

    public int? GetSegmentOverrideIndex() {
        return _segmentOverrideIndex;
    }

    public SegmentRegisters GetSegmentRegisters() {
        return _segmentRegisters;
    }

    public ushort GetSI() {
        return _registers.GetRegister(Registers.SiIndex);
    }

    public bool GetSignFlag() {
        return _flags.GetFlag(Flags.Sign);
    }

    public ushort GetSP() {
        return _registers.GetRegister(Registers.SpIndex);
    }

    public ushort GetSS() {
        return _segmentRegisters.GetRegister(SegmentRegisters.SsIndex);
    }

    public uint GetStackPhysicalAddress() {
        return MemoryUtils.ToPhysicalAddress(this.GetSS(), this.GetSP());
    }

    public bool GetTrapFlag() {
        return _flags.GetFlag(Flags.Trap);
    }

    public bool GetZeroFlag() {
        return _flags.GetFlag(Flags.Zero);
    }

    public void IncCycles() {
        _cycles++;
    }

    public void ResetCurrentInstructionPrefix() {
        this._currentInstructionPrefix = "";
    }

    public void SetAH(byte value) {
        _registers.SetRegister8H(Registers.AxIndex, value);
    }

    public void SetAL(byte value) {
        _registers.SetRegister8L(Registers.AxIndex, value);
    }

    public void SetAuxiliaryFlag(bool value) {
        _flags.SetFlag(Flags.Auxiliary, value);
    }

    public void SetAX(ushort value) {
        _registers.SetRegister(Registers.AxIndex, value);
    }

    public void SetBH(byte value) {
        _registers.SetRegister8H(Registers.BxIndex, value);
    }

    public void SetBL(byte value) {
        _registers.SetRegister8L(Registers.BxIndex, value);
    }

    public void SetBP(ushort value) {
        _registers.SetRegister(Registers.BpIndex, value);
    }

    public void SetBX(ushort value) {
        _registers.SetRegister(Registers.BxIndex, value);
    }

    public void SetCarryFlag(bool value) {
        _flags.SetFlag(Flags.Carry, value);
    }

    public void SetCH(byte value) {
        _registers.SetRegister8H(Registers.CxIndex, value);
    }

    public void SetCL(byte value) {
        _registers.SetRegister8L(Registers.CxIndex, value);
    }

    public void SetContinueZeroFlagValue(bool? continueZeroFlagValue) {
        this._continueZeroFlagValue = continueZeroFlagValue;
    }

    public void SetCS(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.CsIndex, value);
    }

    public void SetCurrentInstructionName(string currentInstructionName) {
        this._currentInstructionName = currentInstructionName;
    }

    public void SetCX(ushort value) {
        _registers.SetRegister(Registers.CxIndex, value);
    }

    public void SetCycles(long cycles) {
        this._cycles = cycles;
    }

    public void SetDH(byte value) {
        _registers.SetRegister8H(Registers.DxIndex, value);
    }

    public void SetDI(ushort value) {
        _registers.SetRegister(Registers.DiIndex, value);
    }

    public void SetDirectionFlag(bool value) {
        _flags.SetFlag(Flags.Direction, value);
    }

    public void SetDL(byte value) {
        _registers.SetRegister8L(Registers.DxIndex, value);
    }

    public void SetDS(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.DsIndex, value);
    }

    public void SetDX(ushort value) {
        _registers.SetRegister(Registers.DxIndex, value);
    }

    public void SetES(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.EsIndex, value);
    }

    public void SetFS(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.FsIndex, value);
    }

    public void SetGS(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.GsIndex, value);
    }

    public void SetInterruptFlag(bool value) {
        _flags.SetFlag(Flags.Interrupt, value);
    }

    public void SetIP(ushort value) {
        _ip = value;
    }

    public void SetOverflowFlag(bool value) {
        _flags.SetFlag(Flags.Overflow, value);
    }

    public void SetParityFlag(bool value) {
        _flags.SetFlag(Flags.Parity, value);
    }

    public void SetSegmentOverrideIndex(int? segmentOverrideIndex) {
        this._segmentOverrideIndex = segmentOverrideIndex;
    }

    public void SetSI(ushort value) {
        _registers.SetRegister(Registers.SiIndex, value);
    }

    public void SetSignFlag(bool value) {
        _flags.SetFlag(Flags.Sign, value);
    }

    public void SetSP(ushort value) {
        _registers.SetRegister(Registers.SpIndex, value);
    }

    public void SetSS(ushort value) {
        _segmentRegisters.SetRegister(SegmentRegisters.SsIndex, value);
    }

    public void SetTrapFlag(bool value) {
        _flags.SetFlag(Flags.Trap, value);
    }

    public void SetZeroFlag(bool value) {
        _flags.SetFlag(Flags.Zero, value);
    }

    public override string ToString() {
        return DumpRegFlags();
    }
}