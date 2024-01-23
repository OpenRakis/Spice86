using Spice86.Core.Emulator.CPU.Exceptions;

namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

using Spice86.Core.Emulator.CPU.Registers;

public class Instructions16 : Instructions16Or32 {
    private readonly Alu16 _alu16;

    public Instructions16(Cpu cpu, Memory.IMemory memory, ModRM modRm)
        : base(cpu, memory, modRm) {
        _alu16 = new Alu16(cpu.State);

    }

    private UInt16RegistersIndexer UInt16Registers => State.GeneralRegisters.UInt16;

    public override void AddRmReg() {
        // ADD rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Add(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AddRegRm() {
        // ADD rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Add(ModRM.R16, ModRM.GetRm16());
    }

    public override void AddAccImm() {
        // ADD AX iw
        State.AX = _alu16.Add(State.AX, Cpu.NextUint16());
    }

    public override void OrRmReg() {
        // OR rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Or(ModRM.GetRm16(), ModRM.R16));
    }

    public override void OrRegRm() {
        // OR rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Or(ModRM.R16, ModRM.GetRm16());
    }

    public override void OrAccImm() {
        // OR AX iw
        State.AX = _alu16.Or(State.AX, Cpu.NextUint16());
    }

    public override void AdcRmReg() {
        // ADC rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Adc(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AdcRegRm() {
        // ADC rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Adc(ModRM.R16, ModRM.GetRm16());
    }

    public override void AdcAccImm() {
        // ADC AX iw
        State.AX = _alu16.Adc(State.AX, Cpu.NextUint16());
    }

    public override void SbbRmReg() {
        // SBB rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Sbb(ModRM.GetRm16(), ModRM.R16));
    }

    public override void SbbRegRm() {
        // SBB rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Sbb(ModRM.R16, ModRM.GetRm16());
    }

    public override void SbbAccImm() {
        // SBB AX iw
        State.AX = _alu16.Sbb(State.AX, Cpu.NextUint16());
    }

    public override void AndRmReg() {
        // AND rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.And(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AndRegRm() {
        // AND rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.And(ModRM.R16, ModRM.GetRm16());
    }

    public override void AndAccImm() {
        // AND AX ib
        State.AX = _alu16.And(State.AX, Cpu.NextUint16());
    }

    public override void SubRmReg() {
        // SUB rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Sub(ModRM.GetRm16(), ModRM.R16));
    }

    public override void SubRegRm() {
        // SUB rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Sub(ModRM.R16, ModRM.GetRm16());
    }

    public override void SubAccImm() {
        // SUB AX iw
        State.AX = _alu16.Sub(State.AX, Cpu.NextUint16());
    }

    public override void XorRmReg() {
        // XOR rmw rw
        ModRM.Read();
        ModRM.SetRm16(_alu16.Xor(ModRM.GetRm16(), ModRM.R16));
    }

    public override void XorRegRm() {
        // XOR rw rmw
        ModRM.Read();
        ModRM.R16 = _alu16.Xor(ModRM.R16, ModRM.GetRm16());
    }

    public override void XorAccImm() {
        // XOR AX iw
        State.AX = _alu16.Xor(State.AX, Cpu.NextUint16());
    }

    public override void CmpRmReg() {
        // CMP rmw rw
        ModRM.Read();
        _alu16.Sub(ModRM.GetRm16(), ModRM.R16);
    }

    public override void CmpRegRm() {
        // CMP rw rmw
        ModRM.Read();
        _alu16.Sub(ModRM.R16, ModRM.GetRm16());
    }

    public override void CmpAccImm() {
        // CMP AX iw
        _alu16.Sub(State.AX, Cpu.NextUint16());
    }

    public override void IncReg(int regIndex) {
        // INC regIndex
        UInt16Registers[regIndex] = _alu16.Inc(UInt16Registers[regIndex]);
    }

    public override void DecReg(int regIndex) {
        // DEC regIndex
        UInt16Registers[regIndex] = _alu16.Dec(UInt16Registers[regIndex]);
    }

    public override void PushReg(int regIndex) {
        // PUSH regIndex
        Stack.Push16(UInt16Registers[regIndex]);
    }

    public override void PopReg(int regIndex) {
        // POP regIndex
        UInt16Registers[regIndex] = Stack.Pop16();
    }

    public override void Pusha() {
        ushort sp = State.SP;
        Stack.Push16(State.AX);
        Stack.Push16(State.CX);
        Stack.Push16(State.DX);
        Stack.Push16(State.BX);
        Stack.Push16(sp);
        Stack.Push16(State.BP);
        Stack.Push16(State.SI);
        Stack.Push16(State.DI);
    }

    public override void Popa() {
        State.DI = Stack.Pop16();
        State.SI = Stack.Pop16();
        State.BP = Stack.Pop16();
        // not restoring SP
        Stack.Pop16();
        State.BX = Stack.Pop16();
        State.DX = Stack.Pop16();
        State.CX = Stack.Pop16();
        State.AX = Stack.Pop16();
    }

    // Push immediate value
    public override void PushImm() {
        // PUSH Imm16
        Stack.Push16(Cpu.NextUint16());
    }

    // Push Sign extended 8bit immediate value
    public override void PushImm8SignExtended() {
        // sign extend it to 16 bits
        short signedValue = (sbyte)Cpu.NextUint8();
        ushort value = (ushort)signedValue;
        Stack.Push16(value);
    }


    public override void ImulRmImm8() {
        // IMUL16 rm16 Imm8
        ModRM.Read();
        ImulRmVal(Cpu.NextUint8());
    }

    public override void ImulRmImm16Or32() {
        // IMUL16 rm16 Imm16
        ModRM.Read();
        ImulRmVal((short)Cpu.NextUint16());
    }

    private void ImulRmVal(short value) {
        int result = _alu16.Imul(value, (short)ModRM.GetRm16());
        ModRM.R16 = (ushort)result;
    }

    public override void ImulRmReg16Or32() {
        // IMUL16 r16 rm16
        ModRM.Read();
        ImulRmVal((short)ModRM.R16);
    }

    protected override void AdvanceSI() {
        AdvanceSI(State.Direction16);
    }

    protected override void AdvanceDI() {
        AdvanceDI(State.Direction16);
    }

    public override void Movs() {
        ushort value = Memory.UInt16[MemoryAddressOverridableDsSi];
        Memory.UInt16[MemoryAddressEsDi] = value;
        AdvanceSIDI();
    }

    public override void Cmps() {
        ushort value = Memory.UInt16[MemoryAddressOverridableDsSi];
        _alu16.Sub(value, Memory.UInt16[MemoryAddressEsDi]);
        AdvanceSIDI();
    }

    public override void TestRmReg() {
        // TEST rmw rw
        ModRM.Read();
        _alu16.And(ModRM.GetRm16(), ModRM.R16);
    }

    public override void TestAccImm() {
        // TEST AX iw
        _alu16.And(State.AX, Cpu.NextUint16());
    }

    public override void Stos() {
        Memory.UInt16[MemoryAddressEsDi] = State.AX;
        AdvanceDI();
    }

    public override void Lods() {
        State.AX = Memory.UInt16[MemoryAddressOverridableDsSi];
        AdvanceSI();
    }

    public override void Scas() {
        _alu16.Sub(State.AX, Memory.UInt16[MemoryAddressEsDi]);
        AdvanceDI();
    }

    public override void Ins() {
        ushort port = State.DX;
        ushort value = Cpu.In16(port);
        Memory.UInt16[MemoryAddressEsDi] = value;
        AdvanceDI();
    }

    public override void Outs() {
        ushort port = State.DX;
        ushort value = Memory.UInt16[MemoryAddressOverridableDsSi];
        Cpu.Out16(port, value);
        AdvanceSI();
    }

    public override void Grp1(bool signExtendOp2) {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
        ushort op1 = ModRM.GetRm16();
        ushort op2;
        if (signExtendOp2) {
            op2 = (ushort)(sbyte)Cpu.NextUint8();
        } else {
            op2 = Cpu.NextUint16();
        }

        ushort res = groupIndex switch {
            0 => _alu16.Add(op1, op2),
            1 => _alu16.Or(op1, op2),
            2 => _alu16.Adc(op1, op2),
            3 => _alu16.Sbb(op1, op2),
            4 => _alu16.And(op1, op2),
            5 => _alu16.Sub(op1, op2),
            6 => _alu16.Xor(op1, op2),
            7 => _alu16.Sub(op1, op2),
            _ => throw new InvalidGroupIndexException(State, groupIndex)
        };
        // 7 is CMP so no memory to set
        if (groupIndex != 7) {
            ModRM.SetRm16(res);
        }
    }

    public override void Grp2(Grp2CountSource countSource) {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
        ushort value = ModRM.GetRm16();
        byte count = ComputeGrp2Count(countSource);

        ushort res = groupIndex switch {
            0 => _alu16.Rol(value, count),
            1 => _alu16.Ror(value, count),
            2 => _alu16.Rcl(value, count),
            3 => _alu16.Rcr(value, count),
            4 => _alu16.Shl(value, count),
            5 => _alu16.Shr(value, count),
            7 => _alu16.Sar(value, count),
            _ => throw new InvalidGroupIndexException(State, groupIndex)
        };
        ModRM.SetRm16(res);
    }

    protected override void Grp3TestRm() {
        _alu16.And(ModRM.GetRm16(), Cpu.NextUint16());
    }

    protected override void Grp3NotRm() {
        ModRM.SetRm16((ushort)~ModRM.GetRm16());
    }

    protected override void Grp3NegRm() {
        ushort value = ModRM.GetRm16();
        value = _alu16.Sub(0, value);
        ModRM.SetRm16(value);
        State.CarryFlag = value != 0;
    }

    protected override void Grp3MulRmAcc() {
        uint result = _alu16.Mul(State.AX, ModRM.GetRm16());
        // Upper part of the result goes in DX
        State.DX = (ushort)(result >> 16);
        State.AX = (ushort)result;
    }

    protected override void Grp3IMulRmAcc() {
        int result = _alu16.Imul((short)State.AX, (short)ModRM.GetRm16());
        // Upper part of the result goes in DX
        State.DX = (ushort)(result >> 16);
        State.AX = (ushort)result;
    }

    protected override void Grp3DivRmAcc() {
        uint v1 = (uint)(State.DX << 16 | State.AX);
        ushort v2 = ModRM.GetRm16();
        ushort result = _alu16.Div(v1, v2);
        State.AX = result;
        State.DX = (ushort)(v1 % v2);
    }

    protected override void Grp3IdivRmAcc() {
        // no sign extension for v1 as it is already a 32bit value
        int v1 = State.DX << 16 | State.AX;
        short v2 = (short) ModRM.GetRm16();
        short result = _alu16.Idiv(v1, v2);
        State.AX = (ushort)result;
        State.DX = (ushort)(v1 % v2);
    }

    protected override void Grp45RmInc() {
        // INC
        ModRM.SetRm16(_alu16.Inc(ModRM.GetRm16()));
    }

    protected override void Grp45RmDec() {
        // DEC
        ModRM.SetRm16(_alu16.Dec(ModRM.GetRm16()));
    }

    protected override void Grp5RmPush() {
        Stack.Push16(ModRM.GetRm16());
    }

    public override void XchgRm() {
        // XCHG rmw rw
        ModRM.Read();
        ushort value1 = ModRM.GetRm16();
        ushort value2 = ModRM.R16;
        ModRM.R16 = value1;
        ModRM.SetRm16(value2);
    }

    public override void XchgAcc(int regIndex) {
        // XCHG AX regIndex
        (State.AX, UInt16Registers[regIndex]) = (UInt16Registers[regIndex], State.AX);
    }

    public override void MovRmReg() {
        // MOV rmw rw
        ModRM.Read();
        ModRM.SetRm16(ModRM.R16);
    }

    public override void MovRegRm() {
        // MOV rw, rmw
        ModRM.Read();
        ModRM.R16 = ModRM.GetRm16();
    }

    public override void MovRegImm(int regIndex) {
        // MOV reg66(regIndex) iw
        UInt16Registers[regIndex] = Cpu.NextUint16();
    }

    public override void MovAccMoffs() {
        // MOV AX moffs16
        State.AX = Memory.UInt16[DsNextUint16Address];
    }

    public override void MovMoffsAcc() {
        // MOV moffs16 AX
        Memory.UInt16[DsNextUint16Address] = State.AX;
    }

    public override void MovRmImm() {
        // MOV rmw iw
        ModRM.Read();
        ModRM.SetRm16(Cpu.NextUint16());
    }

    public override void MovRmSreg() {
        // MOV rmw sreg
        ModRM.Read();
        ModRM.SetRm16(ModRM.SegmentRegister);
    }

    // Only present in 16 bit
    public void MovSregRm() {
        // MOV sreg rmw
        ModRM.Read();
        if (ModRM.RegisterIndex == SegmentRegisters.CsIndex) {
            throw new CpuInvalidOpcodeException("Attempted to write to CS register with MOV instruction");
        }
        ModRM.SegmentRegister = ModRM.GetRm16();
    }

    public override void Lea() {
        ModRM.R16 = ExtractLeaMemoryOffset16();
    }

    public override void PopRm() {
        // POP rmw
        ModRM.Read();
        ModRM.SetRm16(Stack.Pop16());
    }

    public override void Cbw() {
        // CBW, Convert byte to word
        short shortValue = (sbyte)State.AL;
        State.AX = (ushort)shortValue;
    }

    public override void Cwd() {
        // CWD, Sign extend AX into DX (word to dword)
        if (State.AX >= 0x8000) {
            State.DX = 0xFFFF;
        } else {
            State.DX = 0;
        }
    }

    public override void Pushf() {
        // PUSHF
        Stack.Push16((ushort)State.Flags.FlagRegister);
    }

    public override void Popf() {
        // POPF
        State.Flags.FlagRegister = Stack.Pop16();
    }

    protected override ushort DoLxsAndReturnSegmentValue() {
        uint memoryAddress = ReadLxsMemoryAddress();
        (ushort segment, ModRM.R16) = Memory.SegmentedAddress[memoryAddress];
        return segment;
    }

    public override void InImm8() {
        // IN AX Imm8
        byte port = Cpu.NextUint8();
        State.AX = Cpu.In16(port);
    }

    public override void OutImm8() {
        // OUT AX Imm8
        byte port = Cpu.NextUint8();
        ushort value = State.AX;
        Cpu.Out16(port, value);
    }

    public override void InDx() {
        // IN AX DX
        State.AX = Cpu.In16(State.DX);
    }

    public override void OutDx() {
        // OUT DX AX
        Cpu.Out16(State.DX, State.AX);
    }

    public override void Enter() {
        ushort storage = Cpu.NextUint16();
        byte level = Cpu.NextUint8();
        Stack.Push16(State.BP);
        level &= 0x1f;
        ushort framePtr = State.SP;
        const int operandOffset = 2;
        for (int i = 0; i < level; i++) {
            State.BP -= operandOffset;
            Stack.Push16(State.BP);
        }

        State.BP = framePtr;
        State.SP -= storage;
    }

    public override void Leave() {
        State.SP = State.BP;
        State.BP = Stack.Pop16();
    }

    public override void Shld(Grp2CountSource countSource) {
        ModRM.Read();
        byte count = ComputeGrp2Count(countSource);

        ushort source = ModRM.R16;
        ushort destination = ModRM.GetRm16();
        ushort value = _alu16.Shld(destination, source, count);
        ModRM.SetRm16(value);
    }

    public override void MovzxByte() {
        ModRM.Read();
        ModRM.R16 = ModRM.GetRm8();
    }

    public override void MovsxByte() {
        ModRM.Read();
        ModRM.R16 = (ushort)(sbyte)ModRM.GetRm8();
    }
}