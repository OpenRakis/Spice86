namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for SETcc instructions (opcodes 0F 90-9F).
/// Sets RM8 to 1 if condition is true, 0 otherwise.
/// </summary>
public class SetRmccParser : BaseInstructionParser {
    private static readonly InstructionOperation[] SetOperations = {
        InstructionOperation.SETO,
        InstructionOperation.SETNO,
        InstructionOperation.SETB,
        InstructionOperation.SETAE,
        InstructionOperation.SETE,
        InstructionOperation.SETNE,
        InstructionOperation.SETBE,
        InstructionOperation.SETA,
        InstructionOperation.SETS,
        InstructionOperation.SETNS,
        InstructionOperation.SETP,
        InstructionOperation.SETNP,
        InstructionOperation.SETL,
        InstructionOperation.SETGE,
        InstructionOperation.SETLE,
        InstructionOperation.SETG,
    };

    public SetRmccParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, int conditionCode) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        ValueNode conditionNode = _astBuilder.Flag.BuildSetCondition(conditionCode);
        ValueNode destNode = _astBuilder.ModRm.RmToNode(DataType.UINT8, modRmContext);
        IfElseNode ifElseNode = _astBuilder.ControlFlow.TernaryAssign(DataType.UINT8, destNode,
            conditionNode,
            _astBuilder.Constant.ToNode((byte)1),
            _astBuilder.Constant.ToNode((byte)0));
        InstructionNode displayAst = new InstructionNode(SetOperations[conditionCode], destNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ifElseNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
