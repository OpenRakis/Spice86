namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class Interrupt3 : CfgInstruction, ICallInstruction {
    public Interrupt3(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, null) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INT, builder.Constant.ToNode((byte)3));
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleInterruptInstruction(this, 3);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        return new InterruptCallNode(this, builder.Constant.ToNode((byte)3));
    }
}