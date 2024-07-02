namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public abstract class BaseGrpOperationParser : BaseInstructionParser {
    public BaseGrpOperationParser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        ModRmContext modRmContext = _modRmParser.ParseNext(context);
        int groupIndex = modRmContext.RegisterIndex;
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }

        return Parse(context, modRmContext, groupIndex);
    }

    protected abstract CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex);
}