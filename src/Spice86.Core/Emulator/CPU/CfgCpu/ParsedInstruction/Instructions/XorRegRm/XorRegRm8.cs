namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.XorRegRm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class XorRegRm8 : InstructionWithModRm {

    public XorRegRm8(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes, modRmContext) {
    }
    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}