namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddAccImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class AddAccImm32 : InstructionWithValueField<uint> {
    public AddAccImm32(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<uint> valueField) : base(address, opcodeField, prefixes, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}