using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

public class Instructions32 : Instructions16Or32 {
    public Instructions32(Machine machine, Alu alu, Cpu cpu, Memory.Memory memory, ModRM modRm) : 
        base(machine, alu, cpu, memory, modRm) {
    }

    public override void AddRmReg() {
        // ADD rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Add32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void AddRegRm() {
        // ADD rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Add32(ModRM.R32, ModRM.GetRm32());
    }

    public override void AddAccImm() {
        // ADD EAX idw
        State.EAX = Alu.Add32(State.EAX, Cpu.NextUint32());
    }

    public override void OrRmReg() {
        // OR rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Or32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void OrRegRm() {
        // OR rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Or32(ModRM.R32, ModRM.GetRm32());
    }

    public override void OrAccImm() {
        // OR EAX idw
        State.EAX = Alu.Or32(State.EAX, Cpu.NextUint32());
    }

    public override void AdcRmReg() {
        // ADC rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Adc32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void AdcRegRm() {
        // ADC rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Adc32(ModRM.R32, ModRM.GetRm32());
    }

    public override void AdcAccImm() {
        // ADC EAX idw
        State.EAX = Alu.Adc32(State.EAX, Cpu.NextUint32());
    }

    public override void SbbRmReg() {
        // SBB rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Sbb32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void SbbRegRm() {
        // SBB rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Sbb32(ModRM.R32, ModRM.GetRm32());
    }

    public override void SbbAccImm() {
        // SBB EAX idw
        State.EAX = Alu.Sbb32(State.EAX, Cpu.NextUint32());
    }

    public override void AndRmReg() {
        // AND rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.And32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void AndRegRm() {
        // AND rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.And32(ModRM.R32, ModRM.GetRm32());
    }

    public override void AndAccImm() {
        // AND EAX idw
        State.EAX = Alu.And32(State.EAX, Cpu.NextUint32());
    }

    public override void SubRmReg() {
        // SUB rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Sub32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void SubRegRm() {
        // SUB rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Sub32(ModRM.R32, ModRM.GetRm32());
    }

    public override void SubAccImm() {
        // SUB EAX idw
        State.EAX = Alu.Sub32(State.EAX, Cpu.NextUint32());
    }

    public override void XorRmReg() {
        // XOR rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(Alu.Xor32(ModRM.GetRm32(), ModRM.R32));
    }

    public override void XorRegRm() {
        // XOR rdw rmdw
        ModRM.Read();
        ModRM.R32 = Alu.Xor32(ModRM.R32, ModRM.GetRm32());
    }

    public override void XorAccImm() {
        // XOR EAX idw
        State.EAX = Alu.Xor32(State.EAX, Cpu.NextUint32());
    }

    public override void CmpRmReg() {
        // CMP rmdw rdw
        ModRM.Read();
        Alu.Sub32(ModRM.GetRm32(), ModRM.R32);
    }

    public override void CmpRegRm() {
        // CMP rdw rmdw
        ModRM.Read();
        Alu.Sub32(ModRM.R32, ModRM.GetRm32());
    }

    public override void CmpAccImm() {
        // CMP EAX idw
        Alu.Sub32(State.EAX, Cpu.NextUint32());
    }

    public override void IncReg(int regIndex) {
        // INC regIndex
        State.Registers.SetRegister32(regIndex, Alu.Inc32(State.Registers.GetRegister32(regIndex)));
    }

    public override void DecReg(int regIndex) {
        // DEC regIndex
        State.Registers.SetRegister32(regIndex, Alu.Dec32(State.Registers.GetRegister32(regIndex)));
    }

    public override void PushReg(int regIndex) {
        // PUSH regIndex
        Stack.Push32(State.Registers.GetRegister32(regIndex));
    }

    public override void PopReg(int regIndex) {
        // POP regIndex
        State.Registers.SetRegister32(regIndex, Stack.Pop32());
    }

    public override void Pusha() {
        uint sp = State.ESP;
        Stack.Push32(State.EAX);
        Stack.Push32(State.ECX);
        Stack.Push32(State.EDX);
        Stack.Push32(State.EBX);
        Stack.Push32(sp);
        Stack.Push32(State.EBP);
        Stack.Push32(State.ESI);
        Stack.Push32(State.EDI);
    }

    public override void Popa() {
        State.EDI = Stack.Pop32();
        State.ESI = Stack.Pop32();
        State.EBP = Stack.Pop32();
        // not restoring SP
        Stack.Pop32();
        State.EBX = Stack.Pop32();
        State.EDX = Stack.Pop32();
        State.ECX = Stack.Pop32();
        State.EAX = Stack.Pop32();
    }

    // Push immediate value
    public override void PushImm() {
        // PUSH Imm32
        Stack.Push32(Cpu.NextUint32());
    }
    
    // Push Sign extended 8bit immediate value
    public override void PushImm8SignExtended() {
        // sign extend it to 16 bits
        short signedValue = (sbyte)Cpu.NextUint8();
        uint value = (uint)signedValue;
        Stack.Push32(value);
    }

    public override void ImulRmImm8() {
        // IMUL32 rm32 Imm8
        ModRM.Read();
        ImulRmVal(Cpu.NextUint8());
    }
    
    public override void ImulRmImm16Or32() {
        // IMUL32 rm32 Imm32
        ModRM.Read();
        ImulRmVal((int)Cpu.NextUint32());
    }

    private void ImulRmVal(int value) {
        long result = Alu.Imul32(value, (int)ModRM.GetRm32());
        ModRM.R32 = (uint)result;
    }
    
    public override void ImulRmReg16Or32() {
        // IMUL32 r32 rm32
        ModRM.Read();
        ImulRmVal((int)ModRM.R32);
    }

    protected override void AdvanceSI() {
        AdvanceSI(State.Direction32);
    }
    
    protected override void AdvanceDI() {
        AdvanceDI(State.Direction32);
    }

    public override void Movs() {
        uint value = Memory.UInt32[MemoryAddressOverridableDsSi];
        Memory.UInt32[MemoryAddressEsDi] = value;
        AdvanceSIDI();
    }
    
    public override void Cmps() {
        uint value = Memory.UInt32[MemoryAddressOverridableDsSi];
        Alu.Sub32(value, Memory.UInt32[MemoryAddressEsDi]);
        AdvanceSIDI();
    }

    public override void TestRmReg() {
        // TEST rmdw rdw
        ModRM.Read();
        Alu.And32(ModRM.GetRm32(), ModRM.R32);
    }

    public override void TestAccImm() {
        // TEST EAX idw
        Alu.And32(State.EAX, Cpu.NextUint32());
    }
    
    public override void Stos() {
        Memory.UInt32[MemoryAddressEsDi] = State.EAX;
        AdvanceDI();
    }
    
    public override void Lods() {
        State.EAX = Memory.UInt32[MemoryAddressOverridableDsSi];
        AdvanceSI();
    }
    
    public override void Scas() {
        Alu.Sub32(State.EAX, Memory.UInt32[MemoryAddressEsDi]);
        AdvanceDI();
    }
    
    public override void Ins() {
        ushort port = State.DX;
        uint value = Cpu.In32(port);
        Memory.UInt32[MemoryAddressEsDi] = value;
        AdvanceDI();
    }

    public override void Outs() {
        ushort port = State.DX;
        uint value = Memory.UInt32[MemoryAddressOverridableDsSi];
        Cpu.Out32(port, value);
        AdvanceSI();
    }
    
    public override void Grp1(bool signExtendOp2) {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        uint op1 = ModRM.GetRm32();
        uint op2;
        if (signExtendOp2) {
            op2 = (ushort)(sbyte)Cpu.NextUint8();
        } else {
            op2 = Cpu.NextUint32();
        }
        uint res = groupIndex switch {
            0 => Alu.Add32(op1, op2),
            1 => Alu.Or32(op1, op2),
            2 => Alu.Adc32(op1, op2),
            3 => Alu.Sbb32(op1, op2),
            4 => Alu.And32(op1, op2),
            5 => Alu.Sub32(op1, op2),
            6 => Alu.Xor32(op1, op2),
            7 => Alu.Sub32(op1, op2),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        // 7 is CMP so no memory to set
        if (groupIndex != 7) {
            ModRM.SetRm32(res);
        }
    }
    
    public override void Grp2(Grp2CountSource countSource) {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        uint value = ModRM.GetRm32();
        byte count = ComputeGrp2Count(countSource);

        uint res = groupIndex switch {
            0 => Alu.Rol32(value, count),
            1 => Alu.Ror32(value, count),
            2 => Alu.Rcl32(value, count),
            3 => Alu.Rcr32(value, count),
            4 => Alu.Shl32(value, count),
            5 => Alu.Shr32(value, count),
            7 => Alu.Sar32(value, count),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        ModRM.SetRm32(res);
    }
    
    
    protected override void Grp3TestRm() {
        Alu.And32(ModRM.GetRm32(), Cpu.NextUint32());
    }

    protected override void Grp3NotRm() {
        ModRM.SetRm32(~ModRM.GetRm32());
    }
    
    protected override void Grp3NegRm() {
        uint value = ModRM.GetRm32();
        value = Alu.Sub32(0, value);
        ModRM.SetRm32(value);
        State.CarryFlag = value != 0;
    }

    protected override void Grp3MulRmAcc() {
        ulong result = Alu.Mul32(State.EAX, ModRM.GetRm32());
        // Upper part of the result goes in EDX
        State.EDX = (uint)(result >> 32);
        State.EAX = (uint)result;
    }

    protected override void Grp3IMulRmAcc() {
        long result = Alu.Imul32((int)State.EAX, (int)ModRM.GetRm32());
        // Upper part of the result goes in EDX
        State.EDX = (uint)(result >> 32);
        State.EAX = (uint)result;
    }

    protected override void Grp3DivRmAcc() {
        ulong v1 = ((ulong)State.EDX << 32) | State.EAX;
        uint v2 = ModRM.GetRm32();
        uint result = Alu.Div32(v1, v2);
        State.EAX = result;
        State.EDX = (uint)(v1 % v2);
    }

    protected override void Grp3IdivRmAcc() {
        // no sign extension for v1 as it is already a 32bit value
        long v1 = (long)(((ulong)State.EDX << 32) | State.EAX);
        int v2 = (int) ModRM.GetRm32();
        int result = Alu.Idiv32(v1, v2);
        State.EAX = (uint)result;
        State.EDX = (uint)(v1 % v2);
    }

    protected override void Grp45RmInc() {
        ModRM.SetRm32(Alu.Inc32(ModRM.GetRm32()));
    }

    protected override void Grp45RmDec() {
        ModRM.SetRm32(Alu.Dec32(ModRM.GetRm32()));
    }

    protected override void Grp5RmPush() {
        Stack.Push32(ModRM.GetRm32());
    }

    public override void XchgRm() {
        // XCHG rmdw rdw
        ModRM.Read();
        uint value1 = ModRM.GetRm32();
        uint value2 = ModRM.R32;
        ModRM.R32 = value1;
        ModRM.SetRm32(value2);
    }
    
    public override void XchgAcc(int regIndex) {
        // XCHG EAX regIndex
        uint value1 = State.EAX;
        State.EAX = State.Registers.GetRegister32(regIndex);
        State.Registers.SetRegister32(regIndex, value1);
    }
    
    public override void MovRmReg() {
        // MOV rmdw rdw
        ModRM.Read();
        ModRM.SetRm32(ModRM.R32);
    }

    public override void MovRegRm() {
        // MOV rdw, rmdw
        ModRM.Read();
        ModRM.R32 = ModRM.GetRm32();
    }
    
    public override void MovRegImm(int regIndex) {
        // MOV reg32(regIndex) idw
        State.Registers.SetRegister32(regIndex, Cpu.NextUint32());
    }
    
    public override void MovAccMoffs() {
        // MOV EAX moffs32
        State.EAX = Memory.UInt32[DsNextUint16Address];
    }
    
    public override void MovMoffsAcc() {
        // MOV moffs32 EAX
        Memory.UInt32[DsNextUint16Address] = State.EAX;
    }
    
    public override void MovRmImm() {
        // MOV rmdw idw
        ModRM.Read();
        ModRM.SetRm32(Cpu.NextUint32());
    }
    
    public override void MovRmSreg() {
        // MOV rmdw sreg
        ModRM.Read();
        ModRM.SetRm32(ModRM.SegmentRegister);
    }

    public override void Lea() {
        ModRM.R32 = ExtractLeaMemoryOffset16();
    }

    public override void PopRm() {
        // POP rmdw
        ModRM.Read();
        ModRM.SetRm32(Stack.Pop32());
    }
    
    public override void Cbw() {
        // CBW, Convert word to dword
        int shortValue = (short)State.AX;
        State.EAX = (uint)shortValue;
    }

    public override void Cwd() {
        // CWD, Sign extend EAX into EDX (dword to qword)
        if (State.EAX >= 0x80000000) {
            State.EDX = 0xFFFFFFFF;
        } else {
            State.EDX = 0;
        }
    }

    public override void Pushf() {
        // PUSHF
        Stack.Push32(State.Flags.FlagRegister & 0x00FCFFFF);
    }

    public override void Popf() {
        // POPF
        State.Flags.FlagRegister = Stack.Pop32();
    }
    
    protected override ushort DoLxsAndReturnSegmentValue() {
        uint memoryAddress = ReadLxsMemoryAddress();
        ModRM.R32 = Memory.UInt32[memoryAddress];
        return Memory.UInt16[memoryAddress + 4];
    }
    
    public override void InImm8() {
        // IN EAX Imm8
        byte port = Cpu.NextUint8();
        State.EAX = Cpu.In32(port);
    }

    public override void OutImm8() {
        // OUT EAX Imm8
        byte port = Cpu.NextUint8();
        uint value = State.EAX;
        Cpu.Out32(port, value);
    }
    
    public override void InDx() {
        // IN EAX DX
        State.EAX = Cpu.In32(State.DX);
    }

    public override void OutDx() {
        // OUT DX EAX
        Cpu.Out32(State.DX, State.EAX);
    }

    public override void Enter() {
        ushort storage = Cpu.NextUint16();
        byte level = Cpu.NextUint8();
        Stack.Push32(State.EBP);
        level &= 0x1f;
        uint framePtr = State.ESP;
        const int operandOffset = 4;
        for (int i = 0; i < level; i++) {
                State.EBP -= operandOffset;
                Stack.Push32(State.EBP);
        }

        State.EBP = framePtr;
        State.ESP -= storage;
    }

    public override void Leave() {
        State.ESP = State.EBP;
        State.EBP = Stack.Pop32();
    }

    public override void Shld(Grp2CountSource countSource) {
        ModRM.Read();
        byte count = ComputeGrp2Count(countSource);
        uint source = ModRM.R32;
        uint destination = ModRM.GetRm32();
        uint value = Alu.Shld32(destination, source, count);
        ModRM.SetRm32(value);
    }

    public void Movsx() {
        ModRM.Read();
        ModRM.R32 = (uint)(short)ModRM.GetRm16();
    }

    public void Movzx() {
        ModRM.Read();
        ModRM.R32 = ModRM.GetRm16();
    }

    public override void MovzxByte() {
        ModRM.Read();
        ModRM.R32 = ModRM.GetRm8();
    }

    public override void MovsxByte() {
        ModRM.Read();
        ModRM.R32 = (uint)(sbyte)ModRM.GetRm8();
    }
}