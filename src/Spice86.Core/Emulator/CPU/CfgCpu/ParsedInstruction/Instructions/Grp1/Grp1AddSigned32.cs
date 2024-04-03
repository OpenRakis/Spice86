namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp1AddSigned32 : Grp1<sbyte> {
    public Grp1AddSigned32(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<sbyte> valueField) : base(address, opcodeField, prefixes,
        modRmContext, valueField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}