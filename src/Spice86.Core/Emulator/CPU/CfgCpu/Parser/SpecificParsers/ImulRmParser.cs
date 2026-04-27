namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>IMUL R, RM (two-operand)</summary>
public class ImulRmParser : OperationModRmParser {
    public ImulRmParser(ParsingTools parsingTools) : base(parsingTools, false) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        BitWidth bitWidth = dataType.BitWidth;
        DataType unsignedType = _astBuilder.UType(bitWidth);
        ValueNode rNode = _astBuilder.ModRm.RToNode(unsignedType, modRmContext);
        ValueNode rSigned = _astBuilder.ModRm.RToNodeSigned(unsignedType, modRmContext);
        ValueNode rmSigned = _astBuilder.ModRm.RmToNodeSigned(unsignedType, modRmContext);
        MethodCallValueNode imulCall = _astBuilder.AluCall(_astBuilder.SType(bitWidth.Double()), bitWidth, "Imul", rSigned, rmSigned);
        ValueNode resultTruncated = _astBuilder.TypeConversion.Convert(unsignedType, imulCall);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.IMUL, rNode, _astBuilder.ModRm.RmToNode(unsignedType, modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(unsignedType, rNode, resultTruncated));
        instr.AttachAsts(displayAst, execAst);
    }
}
