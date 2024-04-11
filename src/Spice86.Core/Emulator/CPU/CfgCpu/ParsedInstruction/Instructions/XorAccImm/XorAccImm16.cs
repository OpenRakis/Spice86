namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.XorAccImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class XorAccImm16 : InstructionWithValueField<ushort> {
    public XorAccImm16(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<ushort> valueField) : base(address, opcodeField, prefixes, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}