using Spice86.Core.Emulator.Memory;

namespace Spice86.Core.Emulator.CPU.InstructionsImpl;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;

public class Instructions16 : Instructions16Or32 {
    public Instructions16(Machine machine, Alu alu, Cpu cpu, Memory.Memory memory, ModRM modRm,
        StaticAddressesRecorder staticAddressesRecorder) : base(machine, alu, cpu, memory, modRm,
        staticAddressesRecorder) {
    }

    public override void AddRmReg() {
        // ADD rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Add16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AddRegRm() {
        // ADD rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Add16(ModRM.R16, ModRM.GetRm16());
    }

    public override void AddAccImm() {
        // ADD AX iw
        State.AX = Alu.Add16(State.AX, Cpu.NextUint16());
    }

    public override void OrRmReg() {
        // OR rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Or16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void OrRegRm() {
        // OR rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Or16(ModRM.R16, ModRM.GetRm16());
    }

    public override void OrAccImm() {
        // OR AX iw
        State.AX = Alu.Or16(State.AX, Cpu.NextUint16());
    }

    public override void AdcRmReg() {
        // ADC rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Adc16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AdcRegRm() {
        // ADC rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Adc16(ModRM.R16, ModRM.GetRm16());
    }

    public override void AdcAccImm() {
        // ADC AX iw
        State.AX = Alu.Adc16(State.AX, Cpu.NextUint16());
    }

    public override void SbbRmReg() {
        // SBB rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Sbb16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void SbbRegRm() {
        // SBB rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Sbb16(ModRM.R16, ModRM.GetRm16());
    }

    public override void SbbAccImm() {
        // SBB AX iw
        State.AX = Alu.Sbb16(State.AX, Cpu.NextUint16());
    }

    public override void AndRmReg() {
        // AND rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.And16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void AndRegRm() {
        // AND rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.And16(ModRM.R16, ModRM.GetRm16());
    }

    public override void AndAccImm() {
        // AND AX ib
        State.AX = Alu.And16(State.AX, Cpu.NextUint16());
    }

    public override void SubRmReg() {
        // SUB rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Sub16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void SubRegRm() {
        // SUB rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Sub16(ModRM.R16, ModRM.GetRm16());
    }

    public override void SubAccImm() {
        // SUB AX iw
        State.AX = Alu.Sub16(State.AX, Cpu.NextUint16());
    }

    public override void XorRmReg() {
        // XOR rmw rw
        ModRM.Read();
        ModRM.SetRm16(Alu.Xor16(ModRM.GetRm16(), ModRM.R16));
    }

    public override void XorRegRm() {
        // XOR rw rmw
        ModRM.Read();
        ModRM.R16 = Alu.Xor16(ModRM.R16, ModRM.GetRm16());
    }

    public override void XorAccImm() {
        // XOR AX iw
        State.AX = Alu.Xor16(State.AX, Cpu.NextUint16());
    }

    public override void CmpRmReg() {
        // CMP rmw rw
        ModRM.Read();
        Alu.Sub16(ModRM.GetRm16(), ModRM.R16);
    }

    public override void CmpRegRm() {
        // CMP rw rmw
        ModRM.Read();
        Alu.Sub16(ModRM.R16, ModRM.GetRm16());
    }

    public override void CmpAccImm() {
        // CMP AX iw
        Alu.Sub16(State.AX, Cpu.NextUint16());
    }

    public override void IncReg(int regIndex) {
        // INC regIndex
        State.Registers.SetRegister16(regIndex, Alu.Inc16(State.Registers.GetRegister16(regIndex)));
    }

    public override void DecReg(int regIndex) {
        // DEC regIndex
        State.Registers.SetRegister16(regIndex, Alu.Dec16(State.Registers.GetRegister16(regIndex)));
    }

    public override void PushReg(int regIndex) {
        // PUSH regIndex
        Stack.Push16(State.Registers.GetRegister16(regIndex));
    }

    public override void PopReg(int regIndex) {
        // POP regIndex
        State.Registers.SetRegister16(regIndex, Stack.Pop16());
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
        int result = Alu.Imul16(value, (short)ModRM.GetRm16());
        ModRM.R16 = (ushort)result;
    }
    
    protected override void AdvanceSI() {
        AdvanceSI(State.Direction16);
    }

    protected override void AdvanceDI() {
        AdvanceDI(State.Direction16);
    }

    public override void Movs() {
        ushort value = Memory.GetUint16(MemoryAddressOverridableDsSi);
        Memory.SetUint16(MemoryAddressEsDi, value);
        AdvanceSIDI();
    }

    public override void Cmps() {
        ushort value = Memory.GetUint16(MemoryAddressOverridableDsSi);
        Alu.Sub16(value, Memory.GetUint16(MemoryAddressEsDi));
        AdvanceSIDI();
    }

    public override void TestRmReg() {
        // TEST rmw rw
        ModRM.Read();
        Alu.And16(ModRM.GetRm16(), ModRM.R16);
    }

    public override void TestAccImm() {
        // TEST AX iw
        Alu.And16(State.AX, Cpu.NextUint16());
    }

    public override void Stos() {
        Memory.SetUint16(MemoryAddressEsDi, State.AX);
        AdvanceDI();
    }

    public override void Lods() {
        State.AX = Memory.GetUint16(MemoryAddressOverridableDsSi);
        AdvanceSI();
    }

    public override void Scas() {
        Alu.Sub16(State.AX, Memory.GetUint16(MemoryAddressEsDi));
        AdvanceDI();
    }

    public override void Ins() {
        ushort port = State.DX;
        ushort value = Cpu.In16(port);
        Memory.SetUint16(MemoryAddressEsDi, value);
        AdvanceDI();
    }

    public override void Outs() {
        ushort port = State.DX;
        ushort value = Memory.GetUint16(MemoryAddressOverridableDsSi);
        Cpu.Out16(port, value);
        AdvanceSI();
    }

    public override void Grp1(bool signExtendOp2) {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        ushort op1 = ModRM.GetRm16();
        ushort op2;
        if (signExtendOp2) {
            op2 = (ushort)(sbyte)Cpu.NextUint8();
        } else {
            op2 = Cpu.NextUint16();
        }

        ushort res = groupIndex switch {
            0 => Alu.Add16(op1, op2),
            1 => Alu.Or16(op1, op2),
            2 => Alu.Adc16(op1, op2),
            3 => Alu.Sbb16(op1, op2),
            4 => Alu.And16(op1, op2),
            5 => Alu.Sub16(op1, op2),
            6 => Alu.Xor16(op1, op2),
            7 => Alu.Sub16(op1, op2),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        // 7 is CMP so no memory to set
        if (groupIndex != 7) {
            ModRM.SetRm16(res);
        }
    }

    public override void Grp2(Grp2CountSource countSource) {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        ushort value = ModRM.GetRm16();
        byte count = ComputeGrp2Count(countSource);

        ushort res = groupIndex switch {
            0 => Alu.Rol16(value, count),
            1 => Alu.Ror16(value, count),
            2 => Alu.Rcl16(value, count),
            3 => Alu.Rcr16(value, count),
            4 => Alu.Shl16(value, count),
            5 => Alu.Shr16(value, count),
            7 => Alu.Sar16(value, count),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        ModRM.SetRm16(res);
    }

    protected override void Grp3TestRm() {
        Alu.And16(ModRM.GetRm16(), Cpu.NextUint16());
    }

    protected override void Grp3NotRm() {
        ModRM.SetRm16((ushort)~ModRM.GetRm16());
    }
    
    protected override void Grp3NegRm() {
        ushort value = ModRM.GetRm16();
        value = Alu.Sub16(0, value);
        ModRM.SetRm16(value);
        State.CarryFlag = value != 0;
    }
    
    protected override void Grp3MulRmAcc() {
        uint result = Alu.Mul16(State.AX, ModRM.GetRm16());
        // Upper part of the result goes in DX
        State.DX = (ushort)(result >> 16);
        State.AX = (ushort)result;
    }

    protected override void Grp3IMulRmAcc() {
        int result = Alu.Imul16((short)State.AX, (short)ModRM.GetRm16());
        // Upper part of the result goes in DX
        State.DX = (ushort)(result >> 16);
        State.AX = (ushort)result;
    }

    protected override void Grp3DivRmAcc() {
        uint v1 = (uint)(State.DX << 16 | State.AX);
        ushort v2 = ModRM.GetRm16();
        ushort? result = Alu.Div16(v1, v2);
        if (result == null) {
            Cpu.HandleDivisionError();
            return;
        }

        State.AX = result.Value;
        State.DX = (ushort)(v1 % v2);
    }

    protected override void Grp3IdivRmAcc() {
        // no sign extension for v1 as it is already a 32bit value
        int v1 = State.DX << 16 | State.AX;
        short v2 = (short) ModRM.GetRm16();
        short? result = Alu.Idiv16(v1, v2);
        if (result == null) {
            Cpu.HandleDivisionError();
            return;
        }

        State.AX = (ushort)result.Value;
        State.DX = (ushort)(v1 % v2);
    }

    protected override void Grp45RmInc() {
        // INC
        ModRM.SetRm16(Alu.Inc16(ModRM.GetRm16()));
    }

    protected override void Grp45RmDec() {
        // DEC
        ModRM.SetRm16(Alu.Dec16(ModRM.GetRm16()));
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
        ushort value1 = State.AX;
        State.AX = State.Registers.GetRegister16(regIndex);
        State.Registers.SetRegister16(regIndex, value1);
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
        State.Registers.SetRegister16(regIndex, Cpu.NextUint16());
    }

    public override void MovAccMoffs() {
        // MOV AX moffs16
        State.AX = Memory.GetUint16(DsNextUint16Address);
        StaticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Word16);
    }

    public override void MovMoffsAcc() {
        // MOV moffs16 AX
        Memory.SetUint16(DsNextUint16Address, State.AX);
        StaticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Word16);
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
        ModRM.R16 = Memory.GetUint16(memoryAddress);
        StaticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Dword32Ptr);
        return Memory.GetUint16(memoryAddress + 2);
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
        ushort value = Alu.Shld16(destination, source, count);
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