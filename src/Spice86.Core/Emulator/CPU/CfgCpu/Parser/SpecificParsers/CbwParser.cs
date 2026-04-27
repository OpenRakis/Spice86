namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>CBW (16-bit) / CWDE (32-bit)</summary>
public class CbwParser : BaseInstructionParser {
    public CbwParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        InstructionOperation displayOp = bitWidth == BitWidth.DWORD_32
            ? InstructionOperation.CBWE
            : InstructionOperation.CBW;
        DataType destType = _astBuilder.UType(bitWidth);
        ValueNode destNode = _astBuilder.Register.Reg(destType, RegisterIndex.AxIndex);
        ValueNode sourceNode = bitWidth == BitWidth.DWORD_32
            ? _astBuilder.Register.Reg16(RegisterIndex.AxIndex)
            : _astBuilder.Register.Reg8(RegisterIndex.AxIndex);
        BitWidth sourceBitWidth = bitWidth == BitWidth.DWORD_32 ? BitWidth.WORD_16 : BitWidth.BYTE_8;
        ValueNode signExtended = _astBuilder.SignExtendToUnsigned(sourceNode, sourceBitWidth, bitWidth);
        InstructionNode displayAst = new InstructionNode(displayOp);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(destType, destNode, signExtended));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
