namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Shared.Emulator.Memory;

public class Interrupt : InstructionWithValueField<byte> {
    public Interrupt(SegmentedAddress address, InstructionField<ushort> opcodeField,
        InstructionField<byte> valueField) : base(address, opcodeField, valueField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleInterruptInstruction(this, ValueField.Value);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INT, builder.InstructionField.ToNode(ValueField)!);
    }
}