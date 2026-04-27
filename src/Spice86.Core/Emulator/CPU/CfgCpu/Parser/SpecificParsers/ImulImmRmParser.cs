namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>IMUL R, RM, IMM (immediate is full-width signed or 8-bit signed)</summary>
public class ImulImmRmParser : BaseInstructionParser {
    public ImulImmRmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, bool imm8) {
        (CfgInstruction instr, DataType unsignedType, BitWidth bitWidth, ModRmContext modRmContext) = ParseModRm(context, false, 1);
        DataType signedType = _astBuilder.SType(bitWidth);
        BitWidth immWidth = imm8 ? BitWidth.BYTE_8 : bitWidth;
        ValueNode immNode = ReadSignedImmediate(instr, immWidth);
        ValueNode rNode = _astBuilder.ModRm.RToNode(unsignedType, modRmContext);
        ValueNode immSigned = _astBuilder.TypeConversion.Convert(signedType, immNode);
        ValueNode rmSigned = _astBuilder.ModRm.RmToNodeSigned(unsignedType, modRmContext);
        MethodCallValueNode imulCall = _astBuilder.AluCall(_astBuilder.SType(bitWidth.Double()), bitWidth, "Imul", immSigned, rmSigned);
        ValueNode resultTruncated = _astBuilder.TypeConversion.Convert(unsignedType, imulCall);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.IMUL,
            rNode, _astBuilder.ModRm.RmToNode(unsignedType, modRmContext), immNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(unsignedType, rNode, resultTruncated));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
