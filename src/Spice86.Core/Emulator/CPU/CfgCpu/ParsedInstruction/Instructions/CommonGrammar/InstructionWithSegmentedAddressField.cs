namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentedAddressField : CfgInstruction {
    public InstructionWithSegmentedAddressField(
        SegmentedAddress address,
        InstructionField<byte> opcodeField,
        InstructionField<SegmentedAddress> segmentedAddressField) :
        base(address, opcodeField) {
        SegmentedAddressField = segmentedAddressField;
        FieldsInOrder.Add(segmentedAddressField);
    }

    public InstructionField<SegmentedAddress> SegmentedAddressField { get; }
}