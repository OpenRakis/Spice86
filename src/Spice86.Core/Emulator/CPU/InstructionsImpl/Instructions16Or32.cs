using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

public abstract class Instructions16Or32 : Instructions {
    protected Instructions16Or32(Cpu cpu, Memory.IMemory memory, ModRM modRm) 
        : base(cpu, memory, modRm) {
    }

    // Inc Reg
    public abstract void IncReg(int regIndex);

    // Dec Reg
    public abstract void DecReg(int regIndex);

    // Push Reg
    public abstract void PushReg(int regIndex);

    // Pop Reg
    public abstract void PopReg(int regIndex);

    // Pusha
    public abstract void Pusha();

    // Popa
    public abstract void Popa();

    // Push immediate value
    public abstract void PushImm();

    // Push Sign extended 8bit immediate value
    public abstract void PushImm8SignExtended();

    // IMUL R <- Rm x Imm8
    public abstract void ImulRmImm8();

    // IMUL R <- Rm x Imm16 / 32
    public abstract void ImulRmImm16Or32();

    public abstract void ImulRmReg16Or32();

    public abstract void MovRmSreg();
    public abstract void Lea();

    public ushort ExtractLeaMemoryOffset16() {
        ModRM.Read();
        ushort? memoryOffset = ModRM.MemoryOffset;
        if (memoryOffset == null) {
            throw new InvalidVMOperationException(State,
                "Memory address was not read by Mod R/M but it is needed for LEA");
        }

        return (ushort)memoryOffset;
    }

    public abstract void PopRm();

    public abstract void XchgAcc(int regIndex);

    public abstract void Cbw();
    public abstract void Cwd();

    public abstract void Pushf();

    public abstract void Popf();

    public abstract void Grp1(bool signExtendOp2);

    public void Grp5() {
        ModRM.Read();
        int groupIndex = ModRM.RegisterIndex;
        switch (groupIndex) {
            case 0:
                Grp45RmInc();
                break;
            case 1:
                Grp45RmDec();
                break;
            case 2:
                Grp5RmCallNear();
                break;
            case 3:
                Grp5RmCallFar();
                break;
            case 4:
                Grp5RmJumpNear();
                break;
            case 5: 
                Grp5RmJumpFar();
                break;
            case 6:
                Grp5RmPush();
                break;
            default:
                throw new InvalidGroupIndexException(State, groupIndex);
        }
    }

    private void Grp5RmCallNear() {
        // NEAR CALL
        ushort callAddress = ModRM.GetRm16();
        Cpu.NearCallWithReturnIpNextInstruction(callAddress);
    }

    private void Grp5RmCallFar() {
        // FAR CALL
        uint? ipAddress = ModRM.MemoryAddress;
        if (ipAddress is null) {
            return;
        }

        (ushort cs, ushort ip) = Memory.SegmentedAddress[ipAddress.Value];
        Cpu.FarCallWithReturnIpNextInstruction(cs, ip);
    }

    private void Grp5RmJumpNear() {
        ushort ip = ModRM.GetRm16();
        Cpu.JumpNear(ip);
    }

    private void Grp5RmJumpFar() {
        uint? ipAddress = ModRM.MemoryAddress;
        if (ipAddress is null) {
            return;
        }
        (ushort cs, ushort ip) = Memory.SegmentedAddress[ipAddress.Value];
        Cpu.JumpFar(cs, ip);
    }

    protected abstract void Grp5RmPush();
    
    protected abstract ushort DoLxsAndReturnSegmentValue();

    protected uint ReadLxsMemoryAddress() {
        // Copy segmented address that is in memory (32bits) into DS/ES and the
        // specified register
        ModRM.Read();
        uint? memoryAddress = ModRM.MemoryAddress;
        if (memoryAddress == null) {
            throw new InvalidVMOperationException(State,
                "Memory address was not read by Mod R/M but it is needed for LES / LDS");
        }

        return (uint)memoryAddress;
    }

    public void Lds() {
        State.DS = DoLxsAndReturnSegmentValue();
    }

    public void Les() {
        State.ES = DoLxsAndReturnSegmentValue();
    }

    public void Lfs() {
        State.FS = DoLxsAndReturnSegmentValue();
    }

    public void Lgs() {
        State.GS = DoLxsAndReturnSegmentValue();
    }

    /// <summary>
    /// https://xem.github.io/minix86/manual/intel-x86-and-64-manual-vol1/o_7281d5ea06a5b67a-159.html
    /// </summary>
    public abstract void Enter();

    public abstract void Leave();
    
    public abstract void Shld(Grp2CountSource countSource);
    
    public abstract void MovzxByte();
    
    public abstract void MovsxByte();
}