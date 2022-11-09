namespace Spice86.Core.Emulator.CPU.InstructionsImpl;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;

public class Instructions8 : Instructions {
    public Instructions8(Machine machine, Alu alu, Cpu cpu, Memory.Memory memory, ModRM modRm,
        StaticAddressesRecorder staticAddressesRecorder) : base(machine, alu, cpu, memory, modRm,
        staticAddressesRecorder) {
    }

    public override void AddRmReg() {
        // ADD rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Add8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AddRegRm() {
        // ADD rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Add8(ModRM.R8, ModRM.GetRm8());
    }

    public override void AddAccImm() {
        // ADD AL ib
        State.AL = Alu.Add8(State.AL, Cpu.NextUint8());
    }

    public override void OrRmReg() {
        // OR rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Or8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void OrRegRm() {
        // OR rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Or8(ModRM.R8, ModRM.GetRm8());
    }

    public override void OrAccImm() {
        // OR AL ib
        State.AL = Alu.Or8(State.AL, Cpu.NextUint8());
    }

    public override void AdcRmReg() {
        // ADC rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Adc8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AdcRegRm() {
        // ADC rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Adc8(ModRM.R8, ModRM.GetRm8());
    }

    public override void AdcAccImm() {
        // ADC AL ib
        State.AL = Alu.Adc8(State.AL, Cpu.NextUint8());
    }

    public override void SbbRmReg() {
        // SBB rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Sbb8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void SbbRegRm() {
        // SBB rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Sbb8(ModRM.R8, ModRM.GetRm8());
    }

    public override void SbbAccImm() {
        // SBB AL ib
        State.AL = Alu.Sbb8(State.AL, Cpu.NextUint8());
    }

    public override void AndRmReg() {
        // AND rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.And8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AndRegRm() {
        // AND rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.And8(ModRM.R8, ModRM.GetRm8());
    }

    public override void AndAccImm() {
        // AND AL ib
        State.AL = Alu.And8(State.AL, Cpu.NextUint8());
    }

    public override void SubRmReg() {
        // SUB rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Sub8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void SubRegRm() {
        // SUB rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Sub8(ModRM.R8, ModRM.GetRm8());
    }

    public override void SubAccImm() {
        // SUB AL ib
        State.AL = Alu.Sub8(State.AL, Cpu.NextUint8());
    }

    public override void XorRmReg() {
        // XOR rmb rb
        ModRM.Read();
        ModRM.SetRm8(Alu.Xor8(ModRM.GetRm8(), ModRM.R8));
    }

    public override void XorRegRm() {
        // XOR rb rmb
        ModRM.Read();
        ModRM.R8 = Alu.Xor8(ModRM.R8, ModRM.GetRm8());
    }

    public override void XorAccImm() {
        // XOR AL ib
        State.AL = Alu.Xor8(State.AL, Cpu.NextUint8());
    }

    public override void CmpRmReg() {
        // CMP rmb rb
        ModRM.Read();
        Alu.Sub8(ModRM.GetRm8(), ModRM.R8);
    }

    public override void CmpRegRm() {
        // CMP rb rmb
        ModRM.Read();
        Alu.Sub8(ModRM.R8, ModRM.GetRm8());
    }

    public override void CmpAccImm() {
        // CMP AL ib
        Alu.Sub8(State.AL, Cpu.NextUint8());
    }


    protected override void AdvanceSI() {
        AdvanceSI(State.Direction8);
    }

    protected override void AdvanceDI() {
        AdvanceDI(State.Direction8);
    }

    public override void Movs() {
        byte value = Memory.GetUint8(MemoryAddressOverridableDsSi);
        Memory.SetUint8(MemoryAddressEsDi, value);
        AdvanceSIDI();
    }

    public override void Cmps() {
        byte value = Memory.GetUint8(MemoryAddressOverridableDsSi);
        Alu.Sub8(value, Memory.GetUint8(MemoryAddressEsDi));
        AdvanceSIDI();
    }

    public override void TestRmReg() {
        // TEST rmb rb
        ModRM.Read();
        Alu.And8(ModRM.GetRm8(), ModRM.R8);
    }

    public override void TestAccImm() {
        // TEST AL ib
        Alu.And8(State.AL, Cpu.NextUint8());
    }

    public override void Stos() {
        Memory.SetUint8(MemoryAddressEsDi, State.AL);
        AdvanceDI();
    }

    public override void Lods() {
        State.AL = Memory.GetUint8(MemoryAddressOverridableDsSi);
        AdvanceSI();
    }

    public override void Scas() {
        Alu.Sub8(State.AL, Memory.GetUint8(MemoryAddressEsDi));
        AdvanceDI();
    }

    public override void Ins() {
        ushort port = State.DX;
        byte value = Cpu.In8(port);
        Memory.SetUint8(MemoryAddressEsDi, value);
        AdvanceDI();
    }

    public override void Outs() {
        ushort port = State.DX;
        byte value = Memory.GetUint8(MemoryAddressOverridableDsSi);
        Cpu.Out8(port, value);
        AdvanceSI();
    }

    public void Grp1() {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        byte op1 = ModRM.GetRm8();
        byte op2 = Cpu.NextUint8();
        byte res = groupIndex switch {
            0 => Alu.Add8(op1, op2),
            1 => Alu.Or8(op1, op2),
            2 => Alu.Adc8(op1, op2),
            3 => Alu.Sbb8(op1, op2),
            4 => Alu.And8(op1, op2),
            5 => Alu.Sub8(op1, op2),
            6 => Alu.Xor8(op1, op2),
            7 => Alu.Sub8(op1, op2),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        // 7 is CMP so no memory to set
        if (groupIndex != 7) {
            ModRM.SetRm8(res);
        }
    }

    public override void Grp2(Grp2CountSource countSource) {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        byte value = ModRM.GetRm8();
        byte count = ComputeGrp2Count(countSource);

        byte res = groupIndex switch {
            0 => Alu.Rol8(value, count),
            1 => Alu.Ror8(value, count),
            2 => Alu.Rcl8(value, count),
            3 => Alu.Rcr8(value, count),
            4 => Alu.Shl8(value, count),
            5 => Alu.Shr8(value, count),
            7 => Alu.Sar8(value, count),
            _ => throw new InvalidGroupIndexException(Machine, groupIndex)
        };
        ModRM.SetRm8(res);
    }

    protected override void Grp3TestRm() {
        Alu.And8(ModRM.GetRm8(), Cpu.NextUint8());
    }

    protected override void Grp3NotRm() {
        ModRM.SetRm8((byte)~ModRM.GetRm8());
    }

    protected override void Grp3NegRm() {
        byte value = ModRM.GetRm8();
        value = Alu.Sub8(0, value);
        ModRM.SetRm8(value);
        State.CarryFlag = value != 0;
    }

    protected override void Grp3MulRmAcc() {
        ushort result = Alu.Mul8(State.AL, ModRM.GetRm8());
        // Upper part of the result goes in AH
        State.AH = (byte)(result >> 8);
        State.AL = (byte)result;
    }

    protected override void Grp3IMulRmAcc() {
        sbyte v2 = (sbyte)ModRM.GetRm8();
        short result = Alu.Imul8((sbyte)State.AL, v2);
        // Upper part of the result goes in AH
        State.AH = (byte)(result >> 8);
        State.AL = (byte)result;
    }

    protected override void Grp3DivRmAcc() {
        ushort v1 = State.AX;
        byte v2 = ModRM.GetRm8();
        byte? result = Alu.Div8(v1, v2);
        if (result == null) {
            Cpu.HandleDivisionError();
            return;
        }

        State.AL = result.Value;
        State.AH = (byte)(v1 % v2);
    }

    protected override void Grp3IdivRmAcc() {
        short v1 = (short)State.AX;
        sbyte v2 = (sbyte)ModRM.GetRm8();
        sbyte? result = Alu.Idiv8(v1, v2);
        if (result == null) {
            Cpu.HandleDivisionError();
            return;
        }

        State.AL = (byte)result.Value;
        State.AH = (byte)(v1 % v2);
    }

    public void Grp4() {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        switch (groupIndex) {
            case 0:
                Grp45RmInc();
                break;
            case 1:
                Grp45RmDec();
                break;
            case 7:
                // Callback, emulator specific instruction FE38 like in dosbox,
                // to allow interrupts to be overridden by the program
                Cpu.Callback(Cpu.NextUint8());
                break;
            default:
                throw new InvalidGroupIndexException(Machine, groupIndex);
        }
    }

    protected override void Grp45RmInc() {
        // INC
        ModRM.SetRm8(Alu.Inc8(ModRM.GetRm8()));
    }

    protected override void Grp45RmDec() {
        // DEC
        ModRM.SetRm8(Alu.Dec8(ModRM.GetRm8()));
    }

    public override void XchgRm() {
        // XCHG rmb rb
        ModRM.Read();
        byte value1 = ModRM.GetRm8();
        byte value2 = ModRM.R8;
        ModRM.R8 = value1;
        ModRM.SetRm8(value2);
    }

    public override void MovRmReg() {
        // MOV rmb rb
        ModRM.Read();
        ModRM.SetRm8(ModRM.R8);
    }

    public override void MovRegRm() {
        // MOV rb, rmb
        ModRM.Read();
        ModRM.R8 = ModRM.GetRm8();
    }

    public override void MovRegImm(int regIndex) {
        // MOV reg8(regIndex) ib
        State.Registers.SetRegisterFromHighLowIndex8(regIndex, Cpu.NextUint8());
    }

    public override void MovAccMoffs() {
        // MOV AL moffs8
        State.AL = Memory.GetUint8(DsNextUint16Address);
        StaticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Byte8);
    }

    public override void MovMoffsAcc() {
        // MOV moffs8 AL
        Memory.SetUint8(DsNextUint16Address, State.AL);
        StaticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Byte8);
    }

    public override void MovRmImm() {
        // MOV rmb ib
        ModRM.Read();
        ModRM.SetRm8(Cpu.NextUint8());
    }

    public void Sahf() {
        // SAHF
        State.Flags.FlagRegister = State.AH;
    }

    public void Lahf() {
        // LAHF
        State.AH = (byte)State.Flags.FlagRegister;
    }

    public void Salc() {
        // Undocumented instruction SALC
        if (State.CarryFlag) {
            State.AL = 0;
        } else {
            State.AL = 0xFF;
        }
    }


    public void Xlat() {
        // XLAT
        uint address = ModRM.GetAddress(SegmentRegisters.DsIndex, State.BX) + State.AL;
        State.AL = Memory.GetUint8(address);
    }


    public override void InImm8() {
        // IN AL Imm8
        byte port = Cpu.NextUint8();
        State.AL = Cpu.In8(port);
    }

    public override void OutImm8() {
        // OUT AL Imm8
        byte port = Cpu.NextUint8();
        byte value = State.AL;
        Cpu.Out8(port, value);
    }

    public override void InDx() {
        // IN AL DX
        State.AL = Cpu.In8(State.DX);
    }

    public override void OutDx() {
        // OUT DX AL
        Cpu.Out8(State.DX, State.AL);
    }

    public void Setcc(bool condition) {
        byte value = (byte)(condition ? 1 : 0);
        ModRM.Read();
        ModRM.SetRm8(value);
    }
}