namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>CALL NEAR IMM / CALL FAR IMM</summary>
public class CallParser : BaseInstructionParser {
    public CallParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParseCallFarImm(ParsingContext context) {
        BitWidth operandBitWidth = context.DefaultWordOperandBitWidth;
        if (context.HasOperandSize32) {
            InstructionField<SegmentedAddress32> addrField32 = _instructionReader.SegmentedAddress32.NextField(true);
            CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
            instr.AddField(addrField32);
            SegmentedAddressNode targetAddress = _astBuilder.SegmentedAddressBuilder.ToNode(addrField32);
            InstructionNode displayAst = new InstructionNode(InstructionOperation.CALL_FAR, _astBuilder.InstructionField.ToNode(addrField32));
            IVisitableAstNode execAst = new CallFarNode(instr, targetAddress, operandBitWidth);
            instr.AttachAsts(displayAst, execAst);
            return instr;
        }
        InstructionField<SegmentedAddress> addrField = _instructionReader.SegmentedAddress16.NextField(true);
        CfgInstruction instr16 = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
        instr16.AddField(addrField);
        SegmentedAddressNode targetAddress16 = _astBuilder.SegmentedAddressBuilder.ToNode(addrField);
        InstructionNode displayAst16 = new InstructionNode(InstructionOperation.CALL_FAR, _astBuilder.InstructionField.ToNode(addrField));
        IVisitableAstNode execAst16 = new CallFarNode(instr16, targetAddress16, operandBitWidth);
        instr16.AttachAsts(displayAst16, execAst16);
        return instr16;
    }

    public CfgInstruction ParseCallNearImm(ParsingContext context) {
        BitWidth offsetWidth = context.DefaultWordOperandBitWidth;
        BitWidth operandBitWidth = context.DefaultWordOperandBitWidth;
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, null) { Kind = InstructionKind.Call };
        (int offsetValue, FieldWithValue offsetField) = ReadSignedOffset(offsetWidth);
        instr.AddField(offsetField);
        ushort targetIp = (ushort)(instr.NextInMemoryAddress32.Offset + offsetValue);
        ValueNode targetIpNode = _astBuilder.Constant.ToNearAddressNode(targetIp, instr.NextInMemoryAddress32.ToSegmentedAddress());
        instr.AttachAsts(
            new InstructionNode(InstructionOperation.CALL_NEAR, targetIpNode),
            new CallNearNode(instr, targetIpNode, operandBitWidth));
        return instr;
    }
}
