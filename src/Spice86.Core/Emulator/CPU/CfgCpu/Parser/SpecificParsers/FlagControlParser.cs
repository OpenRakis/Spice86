namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>CLC, STC, CLI, STI, CLD, STD, CMC</summary>
public class FlagControlParser : BaseInstructionParser {
    public FlagControlParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseCmc(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        CpuFlagNode flagNode = _astBuilder.Flag.Carry();
        UnaryOperationNode notFlag = new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, flagNode);
        BinaryOperationNode flagAssignment = _astBuilder.Assign(DataType.BOOL, flagNode, notFlag);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CMC);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, flagAssignment);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseFlagControl(ParsingContext context, CpuFlagNode flagNode, ulong value, InstructionOperation displayOp) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        BinaryOperationNode flagAssignment = _astBuilder.Assign(DataType.BOOL, flagNode, _astBuilder.Constant.ToNode(DataType.BOOL, value));
        InstructionNode displayAst = new InstructionNode(displayOp);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, flagAssignment);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseSti(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        CpuFlagNode flagNode = _astBuilder.Flag.Interrupt();
        BinaryOperationNode flagAssignment = _astBuilder.Assign(DataType.BOOL, flagNode, _astBuilder.Constant.ToNode(DataType.BOOL, 1UL));
        IVisitableAstNode setInterruptShadowing = _astBuilder.Flag.SetInterruptShadowingIfInterruptDisabled();
        InstructionNode displayAst = new InstructionNode(InstructionOperation.STI);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, setInterruptShadowing, flagAssignment);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
