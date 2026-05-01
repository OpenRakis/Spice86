namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>JMP NEAR IMM / JMP SHORT / JMP FAR IMM</summary>
public class JmpParser : BaseInstructionParser {
    public JmpParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseJmpNearImm(ParsingContext context) {
        BitWidth offsetWidth = context.DefaultWordOperandBitWidth;
        return ParseJmpNear(context, offsetWidth, InstructionOperation.JMP_NEAR);
    }

    public CfgInstruction ParseJmpNearImm8(ParsingContext context) {
        return ParseJmpNear(context, BitWidth.BYTE_8, InstructionOperation.JMP_SHORT);
    }

    public CfgInstruction ParseJmpFarImm(ParsingContext context) {
        InstructionField<SegmentedAddress> addrField = context.HasOperandSize32
            ? _instructionReader.SegmentedAddress32.NextField(true)
            : _instructionReader.SegmentedAddress16.NextField(true);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1) { Kind = InstructionKind.Jump };
        instr.AddField(addrField);
        SegmentedAddress targetAddress = addrField.Value;
        SegmentedAddressNode targetAddressNode = _astBuilder.Constant.ToNode(targetAddress);
        instr.AttachAsts(
            new InstructionNode(InstructionOperation.JMP_FAR, targetAddressNode),
            new JumpFarNode(instr, targetAddressNode));
        return instr;
    }

    private CfgInstruction ParseJmpNear(ParsingContext context, BitWidth offsetWidth, InstructionOperation displayOp) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1) { Kind = InstructionKind.Jump };
        (int offsetValue, FieldWithValue offsetField) = ReadSignedOffset(offsetWidth);
        instr.AddField(offsetField);
        ushort targetIp = (ushort)(instr.NextInMemoryAddress.Offset + offsetValue);
        ValueNode targetIpNode = _astBuilder.Constant.ToNearAddressNode(targetIp, instr.NextInMemoryAddress);
        instr.AttachAsts(
            new InstructionNode(displayOp, targetIpNode),
            new JumpNearNode(instr, targetIpNode));
        return instr;
    }
}
