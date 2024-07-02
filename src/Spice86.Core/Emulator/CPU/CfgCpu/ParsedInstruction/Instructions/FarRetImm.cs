namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Shared.Emulator.Memory;

public class FarRetImm : InstructionWithValueField<ushort> {

    public FarRetImm(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<ushort> valueField) : base(address, opcodeField, valueField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleFarRet(this, helper.InstructionFieldValueRetriever.GetFieldValue(ValueField));
    }
}