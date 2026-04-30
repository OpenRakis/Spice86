namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>NOP, FWAIT, HLT, CPUID — trivial no-operand instructions</summary>
public class SimpleInstructionParser : BaseInstructionParser {
    public SimpleInstructionParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseHlt(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 0);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.HLT);
        IVisitableAstNode execAst = new HltNode(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseNop(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.NOP);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseFwait(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.FWAIT);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParseCpuid(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CPUID);
        IVisitableAstNode execAst = new CpuidNode(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    /// <summary>
    /// CLTS — Clear Task-Switched flag in CR0. Treated as NOP since CR0 is not emulated in real mode.
    /// Encoded as a 2-byte 0F 06 instruction.
    /// </summary>
    public CfgInstruction ParseClts(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.CLTS);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
