namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class InterruptOverflow : CfgInstruction, ICallInstruction {
    public InterruptOverflow(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, null) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INTO);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        if (helper.State.OverflowFlag) {
            helper.HandleInterruptInstruction(this, 4);
        } else {
            helper.MoveIpToEndOfInstruction(this);
        }
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode overflowFlag = builder.Flag.Overflow();
        ValueNode vectorNumber = builder.Constant.ToNode((byte)4);
        return builder.ControlFlow.ConditionalInterrupt(this, overflowFlag, vectorNumber);
    }
}