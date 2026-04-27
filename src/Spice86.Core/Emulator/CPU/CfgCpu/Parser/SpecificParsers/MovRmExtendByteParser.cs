namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOVZX/MOVSX R16/32, RM8</summary>
public class MovRmExtendByteParser : OperationModRmParser {
    private readonly bool _signExtend;

    public MovRmExtendByteParser(ParsingTools parsingTools, bool signExtend) : base(parsingTools, false) {
        _signExtend = signExtend;
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(_astBuilder.UType(BitWidth.BYTE_8), modRmContext);
        ValueNode extended = _signExtend
            ? _astBuilder.SignExtendToUnsigned(rmNode, BitWidth.BYTE_8, dataType.BitWidth)
            : _astBuilder.TypeConversion.Convert(dataType, rmNode);
        InstructionOperation displayOp = _signExtend ? InstructionOperation.MOVSX : InstructionOperation.MOVSZ;
        InstructionNode displayAst = new InstructionNode(displayOp, rNode, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, rNode, extended));
        instr.AttachAsts(displayAst, execAst);
    }
}
