using Spice86.Core.Emulator.Errors;

namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Utils;

/// <summary>
/// Instruction set of the CPU
/// </summary>
public abstract class Instructions {
    protected readonly Cpu Cpu;
    protected readonly State State;
    protected readonly Stack Stack;
    protected readonly Memory.IMemory Memory;
    protected readonly ModRM ModRM;

    protected uint MemoryAddressEsDi => MemoryUtils.ToPhysicalAddress(State.ES, State.DI);

    protected uint MemoryAddressOverridableDsSi => ModRM.GetAddress(SegmentRegisters.DsIndex, State.SI);

    protected uint DsNextUint16Address => ModRM.GetAddress(SegmentRegisters.DsIndex, Cpu.NextUint16());

    public Instructions(Cpu cpu, Memory.IMemory memory, ModRM modRm) {
        Cpu = cpu;
        State = cpu.State;
        Stack = cpu.Stack;
        Memory = memory;
        ModRM = modRm;
    }
    // Add
    public abstract void AddRmReg();
    public abstract void AddRegRm();
    public abstract void AddAccImm();

    // Or
    public abstract void OrRmReg();
    public abstract void OrRegRm();
    public abstract void OrAccImm();

    // Adc
    public abstract void AdcRmReg();
    public abstract void AdcRegRm();
    public abstract void AdcAccImm();

    // Sbb
    public abstract void SbbRmReg();
    public abstract void SbbRegRm();
    public abstract void SbbAccImm();

    // And
    public abstract void AndRmReg();
    public abstract void AndRegRm();
    public abstract void AndAccImm();

    // Sub
    public abstract void SubRmReg();
    public abstract void SubRegRm();
    public abstract void SubAccImm();

    // Xor
    public abstract void XorRmReg();
    public abstract void XorRegRm();
    public abstract void XorAccImm();

    // Cmp
    public abstract void CmpRmReg();
    public abstract void CmpRegRm();
    public abstract void CmpAccImm();

    // MOVS
    public abstract void Movs();

    // CMPS
    public abstract void Cmps();

    protected void AdvanceSI(short diff) {
        State.SI = (ushort)(State.SI + diff);
    }

    protected void AdvanceDI(short diff) {
        State.DI = (ushort)(State.DI + diff);
    }

    protected abstract void AdvanceSI();

    protected abstract void AdvanceDI();

    protected void AdvanceSIDI() {
        AdvanceSI();
        AdvanceDI();
    }

    // Test
    public abstract void TestRmReg();
    public abstract void TestAccImm();

    // String ops
    public abstract void Stos();
    public abstract void Lods();
    public abstract void Scas();
    public abstract void Ins();
    public abstract void Outs();
    public abstract void XchgRm();

    // Mov
    public abstract void MovRmReg();
    public abstract void MovRegRm();
    public abstract void MovRegImm(int regIndex);
    public abstract void MovAccMoffs();
    public abstract void MovMoffsAcc();
    public abstract void MovRmImm();
    protected byte ComputeGrp2Count(Grp2CountSource countSource) {
        return countSource switch {
            Grp2CountSource.One => 1,
            Grp2CountSource.CL => State.CL,
            Grp2CountSource.NextUint8 => Cpu.NextUint8(),
            _ => throw new InvalidVMOperationException(State, $"Invalid count source {countSource}")
        };
    }

    public abstract void Grp2(Grp2CountSource countSource);

    public void Grp3() {
        ModRM.Read();
        uint groupIndex = ModRM.RegisterIndex;
        switch (groupIndex) {
            case 0:
                Grp3TestRm();
                break;
            case 2:
                Grp3NotRm();
                break;
            case 3:
                Grp3NegRm();
                break;
            case 4:
                Grp3MulRmAcc();
                break;
            case 5:
                Grp3IMulRmAcc();
                break;
            case 6:
                Grp3DivRmAcc();
                break;
            case 7:
                Grp3IdivRmAcc();
                break;
            default:
                throw new InvalidGroupIndexException(State, groupIndex);
        }
    }
    // No ModRM read
    protected abstract void Grp3TestRm();
    protected abstract void Grp3NotRm();
    protected abstract void Grp3NegRm();
    protected abstract void Grp3MulRmAcc();
    protected abstract void Grp3IMulRmAcc();
    protected abstract void Grp3DivRmAcc();
    protected abstract void Grp3IdivRmAcc();
    protected abstract void Grp45RmInc();
    protected abstract void Grp45RmDec();
    public abstract void InImm8();
    public abstract void OutImm8();
    public abstract void InDx();
    public abstract void OutDx();
}