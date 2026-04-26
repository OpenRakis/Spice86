namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>TEST ACC, IMM</summary>
public class TestAccImmParser : BaseInstructionParser {
    public TestAccImmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode immNode = ReadUnsignedImmediate(instr, bitWidth);
        ValueNode accNode = _astBuilder.Register.Accumulator(dataType);
        MethodCallValueNode aluCall = _astBuilder.AluCall(dataType, bitWidth, "And", accNode, immNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.TEST, accNode, immNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.ConditionalAssign(dataType, accNode, aluCall, false));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
