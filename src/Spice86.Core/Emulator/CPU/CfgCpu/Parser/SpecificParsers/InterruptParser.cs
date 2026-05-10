namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>INT, INTO, IRET</summary>
public class InterruptParser : BaseInstructionParser {
    public InterruptParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseInterrupt3(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
        InstructionNode displayAst = new InstructionNode(InstructionOperation.INT, _astBuilder.Constant.ToNode((byte)3));
        IVisitableAstNode execAst = new InterruptCallNode(instr, _astBuilder.Constant.ToNode((byte)3));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseInterruptWithVector(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
        InstructionField<byte> vectorField = _instructionReader.UInt8.NextField(true);
        instr.AddField(vectorField);
        ValueNode vectorNode = _astBuilder.InstructionField.ToNode(vectorField);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.INT, vectorNode);
        IVisitableAstNode execAst = new InterruptCallNode(instr, vectorNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseInterruptOverflow(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
        ValueNode overflowFlag = _astBuilder.Flag.Overflow();
        ValueNode vectorNumber = _astBuilder.Constant.ToNode((byte)4);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.INTO);
        IVisitableAstNode execAst = _astBuilder.ControlFlow.ConditionalInterrupt(instr, overflowFlag, vectorNumber);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseRetInterrupt(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Return };
        instr.EnableCanCauseContextRestore();
        InstructionNode displayAst = new InstructionNode(InstructionOperation.IRET);
        IVisitableAstNode execAst = new ReturnInterruptNode(instr, context.HasOperandSize32 ? BitWidth.DWORD_32 : BitWidth.WORD_16);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
