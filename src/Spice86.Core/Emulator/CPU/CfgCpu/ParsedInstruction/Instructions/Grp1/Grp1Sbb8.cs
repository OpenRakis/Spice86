namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp1Sbb8 : Grp1<byte> {
    public Grp1Sbb8(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<byte> valueField) : base(address, opcodeField, prefixes,
        modRmContext, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}