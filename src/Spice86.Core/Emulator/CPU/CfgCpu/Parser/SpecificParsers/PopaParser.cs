namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>POPA / POPAD</summary>
public class PopaParser : BaseInstructionParser {
    public PopaParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);

        
        MethodCallNode popAll = new StackCallNode(StackOperation.PopAll, bitWidth);

        InstructionNode displayAst = new InstructionNode(bitWidth == BitWidth.DWORD_32 ? InstructionOperation.POPAD : InstructionOperation.POPA);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, popAll);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
