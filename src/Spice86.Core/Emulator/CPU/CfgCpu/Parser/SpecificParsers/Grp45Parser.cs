namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp45;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp45Parser : BaseInstructionParser {
    public Grp45Parser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, int addressSizeFromPrefixes, uint? segmentOverrideFromPrefixes,
        bool hasOperandSize32) {
        bool grp4 = opcodeField.Value is 0xFE;
        ModRmContext modRmContext = _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes);
        uint groupIndex = modRmContext.RegisterIndex;
        if (grp4) {
            return groupIndex switch {
                0 => new Grp45RmInc8(address, opcodeField, prefixes, modRmContext),
                1 => new Grp45RmDec8(address, opcodeField, prefixes, modRmContext),
                // Callback, emulator specific instruction FE38 like in dosbox,
                // to allow interrupts to be overridden by the program
                7 => new Grp4Callback(address, opcodeField, prefixes, modRmContext,
                    _instructionReader.UInt8.NextField(true)),
                _ => throw new InvalidGroupIndexException(_state, groupIndex)
            };
        }

        return groupIndex switch {
            0 => hasOperandSize32
                ? new Grp45RmInc32(address, opcodeField, prefixes, modRmContext)
                : new Grp45RmInc16(address, opcodeField, prefixes, modRmContext),
            1 => hasOperandSize32
                ? new Grp45RmDec32(address, opcodeField, prefixes, modRmContext)
                : new Grp45RmDec16(address, opcodeField, prefixes, modRmContext),
            2 => new Grp5RmCallNear(address, opcodeField, prefixes, modRmContext),
            3 => new Grp5RmCallFar(address, opcodeField, prefixes, modRmContext),
            4 => new Grp5RmJumpNear(address, opcodeField, prefixes, modRmContext),
            5 => new Grp5RmJumpFar(address, opcodeField, prefixes, modRmContext),
            6 => hasOperandSize32
                ? new Grp5RmPush32(address, opcodeField, prefixes, modRmContext)
                : new Grp5RmPush16(address, opcodeField, prefixes, modRmContext),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }
}