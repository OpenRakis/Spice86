namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Shared.Emulator.Memory;

public class HltInstruction : CfgInstruction {
    public HltInstruction(SegmentedAddress address, InstructionField<byte> opcodeField) :
        base(address, opcodeField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}