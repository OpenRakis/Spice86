namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOV RM, IMM</summary>
public class MovRmImmParser : BaseInstructionParser {
    public MovRmImmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, DataType dataType, _, ModRmContext modRmContext) = ParseModRm(context, true, 1);
        ValueNode immNode = ReadUnsignedImmediate(instr, dataType.BitWidth);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, rmNode, immNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, rmNode, immNode));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
