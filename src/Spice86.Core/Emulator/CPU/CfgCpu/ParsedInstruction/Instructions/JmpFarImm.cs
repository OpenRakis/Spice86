namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Shared.Emulator.Memory;

public class JmpFarImm : InstructionWithSegmentedAddressField {
    public JmpFarImm(
        SegmentedAddress address,
        InstructionField<byte> opcodeField,
        InstructionField<SegmentedAddress> segmentedAddressField) :
        base(address, opcodeField, segmentedAddressField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        SegmentedAddress address = helper.InstructionFieldValueRetriever.GetFieldValue(SegmentedAddressField);
        helper.JumpFar(this, address.Segment, address.Offset);
    }
}