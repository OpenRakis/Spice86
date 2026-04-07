namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Hlt : CfgInstruction {
    public Hlt(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 0) {
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.HLT);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.State.IsRunning = false;
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        return new HltNode(this);
    }
}