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

/// <summary>XADD RM, R</summary>
public class XaddRmParser : OperationModRmParser {
    public XaddRmParser(ParsingTools parsingTools) : base(parsingTools, true) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        VariableDeclarationNode sumDecl = _astBuilder.DeclareAluResult(dataType, dataType.BitWidth, "Add", "sum", rNode, rmNode);
        VariableDeclarationNode oldRmDecl = _astBuilder.DeclareVariable(dataType, "oldRm", rmNode);
        BinaryOperationNode assignR = _astBuilder.Assign(dataType, rNode, oldRmDecl.Reference);
        BinaryOperationNode assignRm = _astBuilder.Assign(dataType, rmNode, sumDecl.Reference);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.XADD, rmNode, rNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, sumDecl, oldRmDecl, assignR, assignRm);
        instr.AttachAsts(displayAst, execAst);
    }
}
