namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.AST.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Hlt : CfgInstruction {
    public Hlt(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.State.IsRunning = false;
        helper.MoveIpToEndOfInstruction(this);
        helper.NextNode = null;
    }

    public override InstructionNode ToAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.HLT);
    }
}