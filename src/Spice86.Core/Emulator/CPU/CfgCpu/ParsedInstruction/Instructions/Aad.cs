namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Aad : InstructionWithValueField<byte> {
    public Aad(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<byte> valueField) :
        base(address, opcodeField, new List<InstructionPrefix>(), valueField, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte v2 = helper.InstructionFieldValueRetriever.GetFieldValue(ValueField);
        helper.State.AL = (byte)(helper.State.AL + (helper.State.AH * v2));
        helper.State.AH = 0;
        helper.State.Flags.FlagRegister = 0;
        helper.Alu8.UpdateFlags(helper.State.AL);
        helper.MoveIpAndSetNextNode(this);
    }
    
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.AAD);
    }
}