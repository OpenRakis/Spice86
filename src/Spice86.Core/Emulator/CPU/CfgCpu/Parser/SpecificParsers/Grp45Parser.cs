namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class Grp45Parser : BaseGrpOperationParser {
    public Grp45Parser(BaseInstructionParser other) : base(other) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        bool grp4 = context.OpcodeField.Value is 0xFE;
        if (grp4) {
            return groupIndex switch {
                0 => new Grp45RmInc8(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
                1 => new Grp45RmDec8(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
                // Callback, emulator specific instruction FE38 like in dosbox,
                // to allow interrupts to be overridden by the program
                7 => new Grp4Callback(context.Address, context.OpcodeField, context.Prefixes, modRmContext,
                    _instructionReader.UInt8.NextField(true)),
                _ => throw new InvalidGroupIndexException(_state, groupIndex)
            };
        }

        return groupIndex switch {
            0 => context.HasOperandSize32
                ? new Grp45RmInc32(context.Address, context.OpcodeField, context.Prefixes, modRmContext)
                : new Grp45RmInc16(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            1 => context.HasOperandSize32
                ? new Grp45RmDec32(context.Address, context.OpcodeField, context.Prefixes, modRmContext)
                : new Grp45RmDec16(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            2 => new Grp5RmCallNear(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            3 => new Grp5RmCallFar(context.Address, context.OpcodeField, context.Prefixes, _modRmParser.EnsureNotMode3(modRmContext)),
            4 => new Grp5RmJumpNear(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            5 => new Grp5RmJumpFar(context.Address, context.OpcodeField, context.Prefixes, _modRmParser.EnsureNotMode3(modRmContext)),
            6 => context.HasOperandSize32
                ? new Grp5RmPush32(context.Address, context.OpcodeField, context.Prefixes, modRmContext)
                : new Grp5RmPush16(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }
}