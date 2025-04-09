namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Interrupt3 : CfgInstruction {
    public Interrupt3(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleInterruptInstruction(this, 3);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INT, builder.Constant.ToNode((byte)3));
    }
}