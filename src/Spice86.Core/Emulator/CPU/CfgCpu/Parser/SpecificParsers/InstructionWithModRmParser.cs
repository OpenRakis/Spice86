namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public interface InstructionWithModRmParser {
    public CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext, BitWidth bitWidth);
}