namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovRegImm32 : MovRegImm<uint> {
    public MovRegImm32(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<uint> valueField,
        int registerIndex) : base(address, opcodeField, prefixes, valueField, registerIndex) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}