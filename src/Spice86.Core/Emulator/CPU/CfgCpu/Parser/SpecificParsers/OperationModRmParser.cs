namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Base class for parsers that parse a ModRM byte and build ASTs based on opcode width.
/// Subclasses provide the specific AST-building logic via <see cref="BuildAsts"/>.
/// </summary>
public abstract class OperationModRmParser : BaseInstructionParser {
    private readonly bool _has8;

    protected OperationModRmParser(ParsingTools parsingTools, bool has8) : base(parsingTools) {
        _has8 = has8;
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, DataType dataType, _, ModRmContext modRmContext) = ParseModRm(context, _has8, 1);
        BuildAsts(instr, dataType, modRmContext);
        return instr;
    }

    /// <summary>
    /// Builds display and execution ASTs, then attaches them to the instruction.
    /// </summary>
    protected abstract void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext);
}
