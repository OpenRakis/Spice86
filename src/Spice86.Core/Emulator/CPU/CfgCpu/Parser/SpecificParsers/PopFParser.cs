namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>POPF / POPFD</summary>
public class PopFParser : BaseInstructionParser {
    public PopFParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode popValue = _astBuilder.Stack.Pop(bitWidth);
        ValueNode flagsRegister = _astBuilder.Flag.FlagsRegister(dataType);
        BinaryOperationNode assign = new BinaryOperationNode(dataType, flagsRegister, BinaryOperation.ASSIGN, popValue);
        InstructionNode displayAst = new InstructionNode(bitWidth == BitWidth.DWORD_32 ? InstructionOperation.POPFD : InstructionOperation.POPF);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, assign);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

}
