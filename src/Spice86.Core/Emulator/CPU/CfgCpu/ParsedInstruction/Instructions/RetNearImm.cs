namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class RetNearImm : InstructionWithValueField<ushort>, IReturnInstruction {

    public RetNearImm(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<ushort> valueField) : base(address, opcodeField, valueField) {
    }

    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleNearRet(this, helper.InstructionFieldValueRetriever.GetFieldValue(ValueField));
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.RET_NEAR, builder.InstructionField.ToNode(ValueField)!);
    }
}