namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>IN ACC, IMM8 or OUT IMM8, ACC</summary>
public class IoAccImmParser : BaseInstructionParser {
    public IoAccImmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, bool isInput) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        InstructionField<byte> immField = _instructionReader.UInt8.NextField(false);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(immField);
        ValueNode portNode = _astBuilder.InstructionField.ToNode(immField);
        ValueNode accumulator = _astBuilder.Register.Accumulator(dataType);
        (InstructionNode displayAst, IVisitableAstNode execAst) = BuildIoAsts(instr, dataType, portNode, accumulator, isInput);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
