namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>PUSH IMM16/32 or PUSH IMM8 (sign-extended to 16/32)</summary>
public class PushImmParser : BaseInstructionParser {
    public PushImmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, bool imm8SignExtended) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode displayImmNode;
        ValueNode pushValueNode;
        if (imm8SignExtended) {
            ValueNode rawNode = ReadSignedImmediate(instr, BitWidth.BYTE_8);
            displayImmNode = rawNode;
            pushValueNode = _astBuilder.SignExtendToUnsigned(rawNode, BitWidth.BYTE_8, bitWidth);
        } else {
            ValueNode immNode = ReadUnsignedImmediate(instr, bitWidth);
            displayImmNode = immNode;
            pushValueNode = immNode;
        }
        MethodCallNode pushNode = _astBuilder.Stack.Push(dataType, pushValueNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.PUSH, displayImmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
