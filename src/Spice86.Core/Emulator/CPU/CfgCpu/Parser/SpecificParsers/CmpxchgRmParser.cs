namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>CMPXCHG RM, R</summary>
public class CmpxchgRmParser : OperationModRmParser {
    public CmpxchgRmParser(ParsingTools parsingTools) : base(parsingTools, true) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        ValueNode accNode = _astBuilder.Register.Accumulator(dataType);
        ValueNode zfNode = _astBuilder.Flag.Zero();

        BinaryOperationNode condition = new BinaryOperationNode(DataType.BOOL, rmNode, BinaryOperation.EQUAL, accNode);

        BinaryOperationNode assignZfTrue = _astBuilder.Assign(DataType.BOOL, zfNode, _astBuilder.Constant.ToNode(DataType.BOOL, 1UL));
        BinaryOperationNode assignRm = _astBuilder.Assign(dataType, rmNode, rNode);
        BlockNode trueBlock = new BlockNode(assignZfTrue, assignRm);

        BinaryOperationNode assignZfFalse = _astBuilder.Assign(DataType.BOOL, zfNode, _astBuilder.Constant.ToNode(DataType.BOOL, 0UL));
        BinaryOperationNode assignAcc = _astBuilder.Assign(dataType, accNode, rmNode);
        BlockNode falseBlock = new BlockNode(assignZfFalse, assignAcc);

        IfElseNode ifElse = new IfElseNode(condition, trueBlock, falseBlock);

        InstructionNode displayAst = new InstructionNode(InstructionOperation.CMPXCHG, rmNode, rNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ifElse);
        instr.AttachAsts(displayAst, execAst);
    }
}
