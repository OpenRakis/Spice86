namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class MnemonicModRmParser : BaseInstructionParser {
    public MnemonicModRmParser(BaseInstructionParser other) : base(other) {
    }

    public virtual CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32,
        BitWidth addressWidthFromPrefixes,
        uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressWidthFromPrefixes, segmentOverrideFromPrefixes);
        BitWidth bitWidth = GetBitWidth(opcodeField, hasOperandSize32);
        return Parse(address, opcodeField, prefixes, bitWidth, modRmContext);
    }

    protected abstract CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, BitWidth bitWidth, ModRmContext modRmContext);
}

public abstract class MnemonicModRmParser16Or32 : MnemonicModRmParser {
    public MnemonicModRmParser16Or32(BaseInstructionParser other) : base(other) {
    }

    public override CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32,
        BitWidth addressWidthFromPrefixes,
        uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressWidthFromPrefixes, segmentOverrideFromPrefixes);
        BitWidth bitWidth = GetBitWidth(false, hasOperandSize32);
        return Parse(address, opcodeField, prefixes, bitWidth, modRmContext);
    }
}

[MnemonicModRmParser("MovRmReg")]
partial class MovRmRegParser;

[MnemonicModRmParser("MovRegRm")]
partial class MovRegRmParser;
[MnemonicModRmParser16Or32("MovRmSreg")]
partial class MovRmSregParser;
[MnemonicModRmParser("TestRmReg")]
partial class TestRmRegParser;