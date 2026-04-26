namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOV R, RM or MOV RM, R depending on direction</summary>
public class MovModRmParser : BaseInstructionParser {
    public MovModRmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses a MOV instruction with the specified direction.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, bool regIsDest) {
        (CfgInstruction instr, DataType dataType, _, ModRmContext modRmContext) = ParseModRm(context, true, 1);
        (ValueNode rNode, ValueNode rmNode) = _astBuilder.ModRmOperands(dataType, modRmContext);
        ValueNode dest = regIsDest ? rNode : rmNode;
        ValueNode src = regIsDest ? rmNode : rNode;
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, dest, src);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, dest, src));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
