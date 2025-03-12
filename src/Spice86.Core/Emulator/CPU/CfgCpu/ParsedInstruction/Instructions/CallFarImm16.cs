namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Shared.Emulator.Memory;

public class CallFarImm16 : InstructionWithSegmentedAddressField {
    public CallFarImm16(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        InstructionField<SegmentedAddress> segmentedAddressField) :
        base(address, opcodeField, segmentedAddressField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        SegmentedAddress targetAddress = helper.InstructionFieldValueRetriever.GetFieldValue(SegmentedAddressField);
        helper.FarCallWithReturnIpNextInstruction(this, targetAddress);
    }
}