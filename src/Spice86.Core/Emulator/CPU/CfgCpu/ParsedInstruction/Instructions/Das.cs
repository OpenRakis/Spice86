namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Das : CfgInstruction {
    public Das(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte initialAL = helper.State.AL;
        bool initialCF = helper.State.CarryFlag;
        bool finalAuxillaryFlag = false;
        bool finalCarryFlag = false;
        helper.State.CarryFlag = false;
        if ((helper.State.AL & 0x0F) > 9 || helper.State.AuxiliaryFlag) {
            helper.State.AL = (byte)(helper.State.AL - 6);
            finalCarryFlag = helper.State.CarryFlag || initialCF;
            finalAuxillaryFlag = true;
        }

        if (initialAL > 0x99 || initialCF) {
            helper.State.AL = (byte)(helper.State.AL - 0x60);
            finalCarryFlag = true;
        }

        // Undocumented behaviour
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.State.AuxiliaryFlag = finalAuxillaryFlag;
        helper.State.CarryFlag = finalCarryFlag;
        helper.MoveIpAndSetNextNode(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.DAS);
    }
}