namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

using Spice86.Core.Emulator.CPU.Registers;

public class Instructions8 : Instructions {
    private readonly Alu8 _alu8;
    public Instructions8(Cpu cpu, Memory.IMemory memory, ModRM modRm) :
        base(cpu, memory, modRm) {
        _alu8 = new Alu8(cpu.State);
    }

    public override void AddRmReg() {
        // ADD rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Add(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AddRegRm() {
        // ADD rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Add(ModRM.R8, ModRM.GetRm8());
    }

    public override void AddAccImm() {
        // ADD AL ib
        State.AL = _alu8.Add(State.AL, Cpu.NextUint8());
    }

    public override void OrRmReg() {
        // OR rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Or(ModRM.GetRm8(), ModRM.R8));
    }

    public override void OrRegRm() {
        // OR rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Or(ModRM.R8, ModRM.GetRm8());
    }

    public override void OrAccImm() {
        // OR AL ib
        State.AL = _alu8.Or(State.AL, Cpu.NextUint8());
    }

    public override void AdcRmReg() {
        // ADC rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Adc(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AdcRegRm() {
        // ADC rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Adc(ModRM.R8, ModRM.GetRm8());
    }

    public override void AdcAccImm() {
        // ADC AL ib
        State.AL = _alu8.Adc(State.AL, Cpu.NextUint8());
    }

    public override void SbbRmReg() {
        // SBB rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Sbb(ModRM.GetRm8(), ModRM.R8));
    }

    public override void SbbRegRm() {
        // SBB rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Sbb(ModRM.R8, ModRM.GetRm8());
    }

    public override void SbbAccImm() {
        // SBB AL ib
        State.AL = _alu8.Sbb(State.AL, Cpu.NextUint8());
    }

    public override void AndRmReg() {
        // AND rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.And(ModRM.GetRm8(), ModRM.R8));
    }

    public override void AndRegRm() {
        // AND rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.And(ModRM.R8, ModRM.GetRm8());
    }

    public override void AndAccImm() {
        // AND AL ib
        State.AL = _alu8.And(State.AL, Cpu.NextUint8());
    }

    public override void SubRmReg() {
        // SUB rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Sub(ModRM.GetRm8(), ModRM.R8));
    }

    public override void SubRegRm() {
        // SUB rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Sub(ModRM.R8, ModRM.GetRm8());
    }

    public override void SubAccImm() {
        // SUB AL ib
        State.AL = _alu8.Sub(State.AL, Cpu.NextUint8());
    }

    public override void XorRmReg() {
        // XOR rmb rb
        ModRM.Read();
        ModRM.SetRm8(_alu8.Xor(ModRM.GetRm8(), ModRM.R8));
    }

    public override void XorRegRm() {
        // XOR rb rmb
        ModRM.Read();
        ModRM.R8 = _alu8.Xor(ModRM.R8, ModRM.GetRm8());
    }

    public override void XorAccImm() {
        // XOR AL ib
        State.AL = _alu8.Xor(State.AL, Cpu.NextUint8());
    }

    public override void CmpRmReg() {
        // CMP rmb rb
        ModRM.Read();
        _alu8.Sub(ModRM.GetRm8(), ModRM.R8);
    }

    public override void CmpRegRm() {
        // CMP rb rmb
        ModRM.Read();
        _alu8.Sub(ModRM.R8, ModRM.GetRm8());
    }

    public override void CmpAccImm() {
        // CMP AL ib
        _alu8.Sub(State.AL, Cpu.NextUint8());
    }


    protected override void AdvanceSI() {
        AdvanceSI(State.Direction8);
    }

    protected override void AdvanceDI() {
        AdvanceDI(State.Direction8);
    }

    public override void Movs() {
        byte value = Memory.UInt8[MemoryAddressOverridableDsSi];
        Memory.UInt8[MemoryAddressEsDi] = value;
        AdvanceSIDI();
    }

    public override void Cmps() {
        byte value = Memory.UInt8[MemoryAddressOverridableDsSi];
        _alu8.Sub(value, Memory.UInt8[MemoryAddressEsDi]);
        AdvanceSIDI();
    }

    public override void TestRmReg() {
        // TEST rmb rb
        ModRM.Read();
        _alu8.And(ModRM.GetRm8(), ModRM.R8);
    }

    public override void TestAccImm() {
        // TEST AL ib
        _alu8.And(State.AL, Cpu.NextUint8());
    }

    public override void Stos() {
        Memory.UInt8[MemoryAddressEsDi] = State.AL;
        AdvanceDI();
    }

    public override void Lods() {
        State.AL = Memory.UInt8[MemoryAddressOverridableDsSi];
        AdvanceSI();
    }

    public override void Scas() {
        _alu8.Sub(State.AL, Memory.UInt8[MemoryAddressEsDi]);
        AdvanceDI();
    }

    public override void Ins() {
        ushort port = State.DX;
        byte value = Cpu.In8(port);
        Memory.UInt8[MemoryAddressEsDi] = value;
        AdvanceDI();
    }

    public override void Outs() {
        ushort port = State.DX;
        byte value = Memory.UInt8[MemoryAddressOverridableDsSi];
        Cpu.Out8(port, value);
        AdvanceSI();
    }

    public void Grp1() {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
        byte op1 = ModRM.GetRm8();
        byte op2 = Cpu.NextUint8();
        byte res = groupIndex switch {
            0 => _alu8.Add(op1, op2),
            1 => _alu8.Or(op1, op2),
            2 => _alu8.Adc(op1, op2),
            3 => _alu8.Sbb(op1, op2),
            4 => _alu8.And(op1, op2),
            5 => _alu8.Sub(op1, op2),
            6 => _alu8.Xor(op1, op2),
            7 => _alu8.Sub(op1, op2),
            _ => throw new InvalidGroupIndexException(State, groupIndex)
        };
        // 7 is CMP so no memory to set
        if (groupIndex != 7) {
            ModRM.SetRm8(res);
        }
    }

    public override void Grp2(Grp2CountSource countSource) {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
        byte value = ModRM.GetRm8();
        byte count = ComputeGrp2Count(countSource);

        byte res = groupIndex switch {
            0 => _alu8.Rol(value, count),
            1 => _alu8.Ror(value, count),
            2 => _alu8.Rcl(value, count),
            3 => _alu8.Rcr(value, count),
            4 => _alu8.Shl(value, count),
            5 => _alu8.Shr(value, count),
            7 => _alu8.Sar(value, count),
            _ => throw new InvalidGroupIndexException(State, groupIndex)
        };
        ModRM.SetRm8(res);
    }

    protected override void Grp3TestRm() {
        _alu8.And(ModRM.GetRm8(), Cpu.NextUint8());
    }

    protected override void Grp3NotRm() {
        ModRM.SetRm8((byte)~ModRM.GetRm8());
    }

    protected override void Grp3NegRm() {
        byte value = ModRM.GetRm8();
        value = _alu8.Sub(0, value);
        ModRM.SetRm8(value);
        State.CarryFlag = value != 0;
    }

    protected override void Grp3MulRmAcc() {
        ushort result = _alu8.Mul(State.AL, ModRM.GetRm8());
        // Upper part of the result goes in AH
        State.AH = (byte)(result >> 8);
        State.AL = (byte)result;
    }

    protected override void Grp3IMulRmAcc() {
        sbyte v2 = (sbyte)ModRM.GetRm8();
        short result = _alu8.Imul((sbyte)State.AL, v2);
        // Upper part of the result goes in AH
        State.AH = (byte)(result >> 8);
        State.AL = (byte)result;
    }

    protected override void Grp3DivRmAcc() {
        ushort v1 = State.AX;
        byte v2 = ModRM.GetRm8();
        byte result = _alu8.Div(v1, v2);
        State.AL = result;
        State.AH = (byte)(v1 % v2);
    }

    protected override void Grp3IdivRmAcc() {
        short v1 = (short)State.AX;
        sbyte v2 = (sbyte)ModRM.GetRm8();
        sbyte result = _alu8.Idiv(v1, v2);
        State.AL = (byte)result;
        State.AH = (byte)(v1 % v2);
    }

    public void Grp4() {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
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
                throw new InvalidGroupIndexException(State, groupIndex);
        }
    }

    protected override void Grp45RmInc() {
        // INC
        ModRM.SetRm8(_alu8.Inc(ModRM.GetRm8()));
    }

    protected override void Grp45RmDec() {
        // DEC
        ModRM.SetRm8(_alu8.Dec(ModRM.GetRm8()));
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
        State.GeneralRegisters.UInt8HighLow[regIndex] = Cpu.NextUint8();
    }

    public override void MovAccMoffs() {
        // MOV AL moffs8
        State.AL = Memory.UInt8[DsNextUint16Address];
    }

    public override void MovMoffsAcc() {
        // MOV moffs8 AL
        Memory.UInt8[DsNextUint16Address] = State.AL;
    }

    public override void MovRmImm() {
        // MOV rmb ib
        ModRM.Read();
        ModRM.SetRm8(Cpu.NextUint8());
    }

    public void Sahf() {
        // SAHF
        // EFLAGS(SF:ZF:0:AF:0:PF:1:CF) := AH;
        State.SignFlag = (State.AH & Flags.Sign) == Flags.Sign;
        State.ZeroFlag = (State.AH & Flags.Zero) == Flags.Zero;
        State.AuxiliaryFlag = (State.AH & Flags.Auxiliary) == Flags.Auxiliary;
        State.ParityFlag = (State.AH & Flags.Parity) == Flags.Parity;
        State.CarryFlag = (State.AH & Flags.Carry) == Flags.Carry;
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
        uint address = ModRM.GetAddress((uint)SegmentRegisterIndex.DsIndex, State.BX) + State.AL;
        State.AL = Memory.UInt8[address];
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