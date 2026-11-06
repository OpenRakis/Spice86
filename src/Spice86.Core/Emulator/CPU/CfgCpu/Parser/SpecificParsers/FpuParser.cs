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

/// <summary>8087 control-word initialization and limited status-word storage.</summary>
public class FpuParser : BaseInstructionParser {
    public FpuParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseControl(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        byte modRmByte = modRmContext.ModRmField.Value;
        if (modRmByte is not (0xE1 or 0xE3)) {
            throw new InvalidGroupIndexException(_state, modRmContext.RegisterIndex);
        }
        InstructionOperation operation = modRmByte == 0xE3 ? InstructionOperation.FNINIT : InstructionOperation.FNDISI;
        InstructionNode displayAst = new InstructionNode(operation);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, new FpuControlCallNode(operation));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseControlWord(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        int groupIndex = modRmContext.RegisterIndex;
        if (modRmContext.Mode == 3 || groupIndex is not (5 or 7)) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        InstructionOperation operation = groupIndex == 5 ? InstructionOperation.FLDCW : InstructionOperation.FNSTCW;
        IVisitableAstNode action;
        if (groupIndex == 5) {
            action = new FpuControlCallNode(operation, rmNode);
        } else {
            ValueNode controlWord = new FpuControlWordNode();
            action = _astBuilder.Assign(DataType.UINT16, rmNode, controlWord);
        }
        InstructionNode displayAst = new InstructionNode(operation, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, action);
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
