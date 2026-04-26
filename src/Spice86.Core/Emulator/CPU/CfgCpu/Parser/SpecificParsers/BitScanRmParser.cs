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

/// <summary>BSF/BSR R, RM (bit scan forward/reverse)</summary>
public class BitScanRmParser : OperationModRmParser {
    private readonly InstructionOperation _displayOp;
    private readonly string _scanMethodName;

    public BitScanRmParser(ParsingTools parsingTools, InstructionOperation displayOp, string scanMethodName) : base(parsingTools, false) {
        _displayOp = displayOp;
        _scanMethodName = scanMethodName;
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        ValueNode sourceNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        ValueNode destNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        ValueNode zeroNode = _astBuilder.Constant.ToNode(0);

        BinaryOperationNode conditionNode = new BinaryOperationNode(
            DataType.BOOL, sourceNode, BinaryOperation.NOT_EQUAL, zeroNode);

        MethodCallValueNode bitScanCall = new MethodCallValueNode(
            dataType, null, $"{_scanMethodName}{(int)dataType.BitWidth}", sourceNode);
        BinaryOperationNode assignBitIndex = _astBuilder.Assign(dataType, destNode, bitScanCall);
        BinaryOperationNode setZeroFalse = _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Zero(), _astBuilder.Constant.ToNode(false));
        BlockNode trueCase = new BlockNode(assignBitIndex, setZeroFalse);

        BinaryOperationNode setZeroTrue = _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Zero(), _astBuilder.Constant.ToNode(true));
        BlockNode falseCase = new BlockNode(setZeroTrue);

        IfElseNode ifElseNode = new IfElseNode(conditionNode, trueCase, falseCase);

        InstructionNode displayAst = new InstructionNode(_displayOp, destNode, sourceNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ifElseNode);
        instr.AttachAsts(displayAst, execAst);
    }
}
