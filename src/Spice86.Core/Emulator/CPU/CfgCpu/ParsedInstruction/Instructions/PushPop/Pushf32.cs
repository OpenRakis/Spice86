namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushPop;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Pushf32 : CfgInstruction {
    public Pushf32(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}