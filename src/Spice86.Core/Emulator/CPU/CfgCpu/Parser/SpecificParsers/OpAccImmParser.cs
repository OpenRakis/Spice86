namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class OpAccImmParser  : BaseInstructionParser {
    public OpAccImmParser(BaseInstructionParser other) : base(other) {
    }
    public virtual CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32) {
        BitWidth bitWidth = GetBitWidth(opcodeField, hasOperandSize32);
        return BuildAccImm(address, opcodeField, prefixes, bitWidth);
    }

    protected abstract CfgInstruction BuildAccImm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, BitWidth bitWidth);
}

[OpAccImmParser("Test")]
partial class TestAccImmParser;