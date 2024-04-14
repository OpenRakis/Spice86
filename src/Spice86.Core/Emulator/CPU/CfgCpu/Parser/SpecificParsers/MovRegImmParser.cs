namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovRegImmParser : BaseInstructionParser {
    public MovRegImmParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction ParseMovRegImm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, bool hasOperandSize32) {
        int regIndex = ComputeRegIndex(opcodeField);
        if ((opcodeField.Value & WordMask) == 0) {
            return new MovRegImm8(address, opcodeField, prefixes, _instructionReader.UInt8.NextField(false),
                regIndex);
        }

        if (hasOperandSize32) {
            return new MovRegImm32(address, opcodeField, prefixes, _instructionReader.UInt32.NextField(false),
                regIndex);
        }

        return new MovRegImm16(address, opcodeField, prefixes, _instructionReader.UInt16.NextField(false), regIndex);
    }
}