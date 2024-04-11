namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AndAccImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class AndAccImm8 : InstructionWithValueField<byte> {
    public AndAccImm8(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<byte> valueField) : base(address, opcodeField, prefixes, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}