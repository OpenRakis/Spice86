namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>TEST RM, R</summary>
public class TestRmRegParser : OperationModRmParser {
    public TestRmRegParser(ParsingTools parsingTools) : base(parsingTools, true) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        (ValueNode rNode, ValueNode rmNode) = _astBuilder.ModRmOperands(dataType, modRmContext);
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, dataType.BitWidth, "And", rmNode, rNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.AND, rmNode, rNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.ConditionalAssign(dataType, rmNode, aluCall, false));
        instr.AttachAsts(displayAst, execAst);
    }
}
