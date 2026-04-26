namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>PUSHF / PUSHFD</summary>
public class PushFParser : BaseInstructionParser {
    public PushFParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionNode displayAst;
        IVisitableAstNode execAst;
        if (bitWidth == BitWidth.DWORD_32) {
            ValueNode flagsRegister = _astBuilder.Flag.FlagsRegister(DataType.UINT32);
            ValueNode maskedFlags = new BinaryOperationNode(_astBuilder.UType(BitWidth.DWORD_32), flagsRegister, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode(0x00FCFFFFu));
            IVisitableAstNode pushNode = _astBuilder.Stack.Push(BitWidth.DWORD_32, maskedFlags);
            displayAst = new InstructionNode(InstructionOperation.PUSHFD);
            execAst = _astBuilder.WithIpAdvancement(instr, pushNode);
        } else {
            ValueNode flagsRegister = _astBuilder.Flag.FlagsRegister(DataType.UINT16);
            IVisitableAstNode pushNode = _astBuilder.Stack.Push(BitWidth.WORD_16, flagsRegister);
            displayAst = new InstructionNode(InstructionOperation.PUSHF);
            execAst = _astBuilder.WithIpAdvancement(instr, pushNode);
        }
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
