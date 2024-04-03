namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class AddAccImm16 : AddAccImm<ushort> {
    public AddAccImm16(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<ushort> valueField) : base(address, opcodeField, prefixes, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}