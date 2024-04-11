namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovRegImm8 : MovRegImm<byte> {
    public MovRegImm8(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<byte> valueField,
        int registerIndex) : base(address, opcodeField, prefixes, valueField, registerIndex) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}