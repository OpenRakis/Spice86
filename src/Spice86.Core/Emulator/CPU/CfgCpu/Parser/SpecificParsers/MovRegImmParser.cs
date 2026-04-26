namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class MovRegImmParser : BaseInstructionParser {
    public MovRegImmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, int regIndex, bool is8Bit) {
        BitWidth bitWidth = GetBitWidth(is8Bit, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode immNode = ReadUnsignedImmediate(instr, bitWidth);
        ValueNode regNode = _astBuilder.Register.Reg(dataType, regIndex);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.MOV, regNode, immNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(dataType, regNode, immNode));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}