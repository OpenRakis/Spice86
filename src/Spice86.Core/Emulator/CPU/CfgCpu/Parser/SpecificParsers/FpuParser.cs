namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

/// <summary>FNSTCW, FNSTSW, FNINIT</summary>
public class FpuParser : BaseInstructionParser {
    public FpuParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseFnInitModRmE3(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        byte modRmByte = modRmContext.ModRmField.Value;
        if (modRmByte != 0xE3) {
            throw new InvalidGroupIndexException(_state, modRmContext.RegisterIndex);
        }
        InstructionNode displayAst = new InstructionNode(InstructionOperation.FNINIT);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseFpuStoreWordGroup7(ParsingContext context, ushort value, InstructionOperation displayOp) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        int groupIndex = modRmContext.RegisterIndex;
        if (groupIndex != 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        ValueNode wordValue = _astBuilder.Constant.ToNode(value);
        InstructionNode displayAst = new InstructionNode(displayOp);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT16, rmNode, wordValue));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
