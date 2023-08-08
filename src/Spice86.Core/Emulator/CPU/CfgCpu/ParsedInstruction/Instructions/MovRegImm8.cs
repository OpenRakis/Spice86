namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovRegImm8 : MovRegImm<byte> {
    public MovRegImm8(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<byte> valueField,
        int regIndex) : base(address, opcodeField, prefixes, valueField, regIndex) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}