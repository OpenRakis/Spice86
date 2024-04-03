namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;

using Spice86.Shared.Emulator.Memory;

public class JmpNearImm8 : JmpNearImm<sbyte> {
    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }

    public JmpNearImm8(SegmentedAddress address, InstructionField<byte> opcodeField, InstructionField<sbyte> offsetField) :
        base(address, opcodeField, offsetField) {
    }
}