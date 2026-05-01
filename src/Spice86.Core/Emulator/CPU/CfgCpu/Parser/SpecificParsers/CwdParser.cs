namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>CWD (16-bit) / CDQ (32-bit)</summary>
public class CwdParser : BaseInstructionParser {
    public CwdParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        DataType signedType = _astBuilder.SType(bitWidth);
        DataType unsignedType = _astBuilder.UType(bitWidth);
        int shiftAmount = (int)bitWidth - 1;
        InstructionOperation displayOp = bitWidth == BitWidth.DWORD_32
            ? InstructionOperation.CDQ
            : InstructionOperation.CWD;
        ValueNode destNode = _astBuilder.Register.Reg(unsignedType, RegisterIndex.DxIndex);
        ValueNode sourceSigned = bitWidth == BitWidth.DWORD_32
            ? _astBuilder.Register.Reg32Signed(RegisterIndex.AxIndex)
            : _astBuilder.Register.Reg16Signed(RegisterIndex.AxIndex);
        BinaryOperationNode shiftRight = new BinaryOperationNode(signedType, sourceSigned, BinaryOperation.RIGHT_SHIFT, _astBuilder.Constant.ToNode(shiftAmount));
        ValueNode result = _astBuilder.TypeConversion.Convert(unsignedType, shiftRight);
        InstructionNode displayAst = new InstructionNode(displayOp);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(unsignedType, destNode, result));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
