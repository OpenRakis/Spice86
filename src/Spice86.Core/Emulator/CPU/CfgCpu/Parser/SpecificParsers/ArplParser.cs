namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

/// <summary>ARPL Ew, Gw: raises the r/m selector's RPL to the register selector's RPL if lower, sets ZF if changed.</summary>
public class ArplParser : BaseInstructionParser {
    public ArplParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        ValueNode regNode = _astBuilder.ModRm.RToNode(DataType.UINT16, modRmContext);
        ValueNode zeroFlagNode = _astBuilder.Flag.Zero();

        MethodCallValueNode wasAdjustedCall = new MethodCallValueNode(DataType.BOOL, null,
            nameof(InstructionExecutionHelper.WasPrivilegeLevelAdjusted), rmNode, regNode);
        MethodCallValueNode adjustCall = new MethodCallValueNode(DataType.UINT16, null,
            nameof(InstructionExecutionHelper.AdjustRequestedPrivilegeLevel), rmNode, regNode);

        // Real hardware never writes the r/m operand back to memory unless the RPL was actually
        // raised - an unconditional write-back would incorrectly fault on a read-only destination
        // segment even when the value doesn't change.
        BlockNode trueCase = new BlockNode(
            _astBuilder.Assign(DataType.BOOL, zeroFlagNode, _astBuilder.Constant.ToNode(DataType.BOOL, 1UL)),
            _astBuilder.Assign(DataType.UINT16, rmNode, adjustCall));
        BinaryOperationNode falseCase = _astBuilder.Assign(DataType.BOOL, zeroFlagNode, _astBuilder.Constant.ToNode(DataType.BOOL, 0UL));

        IfElseNode ifElse = new IfElseNode(wasAdjustedCall, trueCase, falseCase);

        InstructionNode displayAst = new InstructionNode(InstructionOperation.ARPL, rmNode, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, ifElse);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
