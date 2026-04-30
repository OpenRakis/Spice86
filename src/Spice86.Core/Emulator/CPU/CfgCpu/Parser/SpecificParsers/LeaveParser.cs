namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>LEAVE / LEAVEW</summary>
public class LeaveParser : BaseInstructionParser {
    public LeaveParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Stack.Leave(bitWidth));
        InstructionNode displayAst = new InstructionNode(bitWidth == BitWidth.DWORD_32 ? InstructionOperation.LEAVEW : InstructionOperation.LEAVE);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
