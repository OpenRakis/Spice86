namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>JCXZ (jump if CX/ECX == 0)</summary>
public class JcxzParser : BaseInstructionParser {
    public JcxzParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (int offsetValue, FieldWithValue offsetField) = ReadSignedOffset(BitWidth.BYTE_8);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(offsetField);
        instr.MaxSuccessorsCount = 2;
        ushort targetIp = (ushort)(instr.NextInMemoryAddress32.Offset + offsetValue);
        ValueNode targetIpNode = _astBuilder.Constant.ToNearAddressNode(targetIp, instr.NextInMemoryAddress32.ToSegmentedAddress());
        DataType counterType = _astBuilder.UType(context.AddressWidthFromPrefixes);
        ValueNode counter = _astBuilder.Register.Reg(counterType, RegisterIndex.CxIndex);
        ValueNode condition = new BinaryOperationNode(
            DataType.BOOL,
            counter,
            BinaryOperation.EQUAL,
            _astBuilder.Constant.ToNode(counter.DataType, 0UL));
        InstructionOperation mnemonicOp = context.AddressWidthFromPrefixes == BitWidth.DWORD_32
            ? InstructionOperation.JECXZ_SHORT
            : InstructionOperation.JCXZ_SHORT;
        InstructionNode displayAst = new InstructionNode(mnemonicOp, targetIpNode);
        IfElseNode execAst = _astBuilder.ControlFlow.ConditionalNearJump(instr, condition, targetIpNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
