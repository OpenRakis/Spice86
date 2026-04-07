namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Nop : CfgInstruction {
    public Nop(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.NOP);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // Well nothing to do :)
        helper.MoveIpToEndOfInstruction(this);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        // NOP - just advance IP
        return builder.WithIpAdvancement(this);
    }
}