namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

/// <summary>LAR (0F 02) / LSL (0F 03): reg &lt;- info about the r/m selector, ZF set if valid.</summary>
public class LarLslParser : BaseInstructionParser {
    public LarLslParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseLar(ParsingContext context) {
        return Parse(context, nameof(InstructionExecutionHelper.IsSelectorValidForLar),
            nameof(InstructionExecutionHelper.LoadAccessRights), InstructionOperation.LAR);
    }

    public CfgInstruction ParseLsl(ParsingContext context) {
        return Parse(context, nameof(InstructionExecutionHelper.IsSelectorValidForLsl),
            nameof(InstructionExecutionHelper.LoadSegmentLimit), InstructionOperation.LSL);
    }

    private CfgInstruction Parse(ParsingContext context, string isValidMethodName, string loadMethodName, InstructionOperation displayOp) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        ValueNode selectorNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        DataType destType = context.HasOperandSize32 ? DataType.UINT32 : DataType.UINT16;
        ValueNode destNode = _astBuilder.ModRm.RToNode(destType, modRmContext);

        MethodCallValueNode isValidCall = new MethodCallValueNode(DataType.BOOL, null, isValidMethodName, selectorNode);
        MethodCallValueNode loadCall = new MethodCallValueNode(DataType.UINT32, null, loadMethodName, selectorNode);
        ValueNode convertedLoad = _astBuilder.TypeConversion.Convert(destType, loadCall);
        BinaryOperationNode assignDest = _astBuilder.Assign(destType, destNode, convertedLoad);
        BinaryOperationNode setZeroTrue = _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Zero(), _astBuilder.Constant.ToNode(true));
        BlockNode trueCase = new BlockNode(assignDest, setZeroTrue);

        BinaryOperationNode setZeroFalse = _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Zero(), _astBuilder.Constant.ToNode(false));
        BlockNode falseCase = new BlockNode(setZeroFalse);

        IfElseNode ifElseNode = new IfElseNode(isValidCall, trueCase, falseCase);

        InstructionNode displayAst = new InstructionNode(displayOp, destNode, selectorNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ifElseNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
