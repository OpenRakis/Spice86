namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;

using Spice86.Shared.Emulator.Memory;

public class JmpNearImm16 : JmpNearImm<short> {
    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }

    public JmpNearImm16(SegmentedAddress address, InstructionField<byte> opcodeField, InstructionField<short> offsetField) :
        base(address, opcodeField, offsetField) {
    }
}