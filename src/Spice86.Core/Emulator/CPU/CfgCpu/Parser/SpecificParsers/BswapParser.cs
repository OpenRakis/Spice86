namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>BSWAP R32</summary>
public class BswapParser : BaseInstructionParser {
    public BswapParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, int regIndex) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode reg = _astBuilder.Register.Reg(DataType.UINT32, regIndex);
        ValueNode swapped = _astBuilder.Bitwise.ByteSwap(reg);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.BSWAP, reg);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT32, reg, swapped));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
