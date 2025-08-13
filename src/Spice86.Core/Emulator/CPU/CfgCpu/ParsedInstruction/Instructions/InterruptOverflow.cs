namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class InterruptOverflow : CfgInstruction, ICallInstruction {
    public InterruptOverflow(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, null) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        if (helper.State.OverflowFlag) {
            helper.HandleInterruptInstruction(this, 4);
        } else {
            helper.MoveIpAndSetNextNode(this);
        }
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INTO);
    }
}