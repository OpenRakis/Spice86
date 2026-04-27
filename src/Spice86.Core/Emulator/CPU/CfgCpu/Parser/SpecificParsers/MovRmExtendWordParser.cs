namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOVZX/MOVSX R32, RM16</summary>
public class MovRmExtendWordParser : BaseInstructionParser {
    public MovRmExtendWordParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, bool signExtend) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        ValueNode rNode = _astBuilder.ModRm.RToNode(DataType.UINT32, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        ValueNode extended;
        InstructionOperation displayOp;
        if (signExtend) {
            extended = _astBuilder.SignExtendToUnsigned(rmNode, BitWidth.WORD_16, BitWidth.DWORD_32);
            displayOp = InstructionOperation.MOVSX;
        } else {
            extended = _astBuilder.TypeConversion.Convert(DataType.UINT32, rmNode);
            displayOp = InstructionOperation.MOVSZ;
        }
        InstructionNode displayAst = new InstructionNode(displayOp, rNode, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT32, rNode, extended));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
