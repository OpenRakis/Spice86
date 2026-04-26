namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>PUSH/POP segment register</summary>
public class SegRegPushPopParser : BaseInstructionParser {
    public SegRegPushPopParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParsePushSReg(ParsingContext context, int segRegIndex) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.SReg(segRegIndex);
        MethodCallNode pushBlock = _astBuilder.Stack.Push(DataType.UINT16, regNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.PUSH, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushBlock);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParsePopSReg(ParsingContext context, int segRegIndex) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.SReg(segRegIndex);
        ValueNode popValue = _astBuilder.Stack.Pop(BitWidth.WORD_16);
        BinaryOperationNode assign = new BinaryOperationNode(DataType.UINT16, regNode, BinaryOperation.ASSIGN, popValue);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.POP, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, assign);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
