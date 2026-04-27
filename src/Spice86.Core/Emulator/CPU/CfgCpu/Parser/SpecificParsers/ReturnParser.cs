namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>RET NEAR / RET FAR</summary>
public class ReturnParser : BaseInstructionParser {
    public ReturnParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseRetNear(ParsingContext context, bool hasImm) {
        return ParseRet(context, hasImm, InstructionOperation.RET_NEAR,
            (instr, bytesToPop, bitWidth) => new ReturnNearNode(instr, bytesToPop, bitWidth));
    }

    public CfgInstruction ParseRetFar(ParsingContext context, bool hasImm) {
        return ParseRet(context, hasImm, InstructionOperation.RET_FAR,
            (instr, bytesToPop, bitWidth) => new ReturnFarNode(instr, bytesToPop, bitWidth));
    }

    private CfgInstruction ParseRet(ParsingContext context, bool hasImm, InstructionOperation operation,
        Func<CfgInstruction, IVisitableAstNode, BitWidth, IVisitableAstNode> execNodeFactory) {
        BitWidth operandBitWidth = context.DefaultWordOperandBitWidth;
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Return };
        ValueNode bytesToPop;
        InstructionNode displayAst;
        if (hasImm) {
            InstructionField<ushort> valueField = _instructionReader.UInt16.NextField(false);
            instr.AddField(valueField);
            bytesToPop = _astBuilder.InstructionField.ToNode(valueField);
            displayAst = new InstructionNode(operation, bytesToPop);
        } else {
            bytesToPop = _astBuilder.Constant.ToNode((ushort)0);
            displayAst = new InstructionNode(operation);
        }
        IVisitableAstNode execAst = execNodeFactory(instr, bytesToPop, operandBitWidth);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
