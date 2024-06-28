namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public  class SetRmccParser : BaseInstructionParser {
    public SetRmccParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        int condition = context.OpcodeField.Value & 0xF;
        ModRmContext modRmContext = _modRmParser.ParseNext(context);
        return condition switch {
            0x0 => new SetRmo(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x1 => new SetRmno(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x2 => new SetRmb(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x3 => new SetRmnb(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x4 => new SetRmz(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x5 => new SetRmnz(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x6 => new SetRmbe(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x7 => new SetRma(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x8 => new SetRms(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0x9 => new SetRmns(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xA => new SetRmp(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xB => new SetRmpo(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xC => new SetRml(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xD => new SetRmge(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xE => new SetRmng(context.Address, context.OpcodeField, context.Prefixes, modRmContext),
            0xF => new SetRmg(context.Address, context.OpcodeField, context.Prefixes, modRmContext)
        };
    }
}