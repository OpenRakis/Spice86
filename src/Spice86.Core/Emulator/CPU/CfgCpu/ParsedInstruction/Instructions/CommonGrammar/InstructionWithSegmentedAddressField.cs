namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class InstructionWithSegmentedAddressField : CfgInstruction {
    public InstructionWithSegmentedAddressField(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<SegmentedAddress> segmentedAddressField,
        int? maxSuccessorsCount) :
        base(address, opcodeField, prefixes, maxSuccessorsCount) {
        SegmentedAddressField = segmentedAddressField;
        AddField(segmentedAddressField);
    }

    public InstructionField<SegmentedAddress> SegmentedAddressField { get; }
}