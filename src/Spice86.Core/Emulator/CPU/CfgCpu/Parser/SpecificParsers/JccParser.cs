namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class JccParser : BaseInstructionParser {
    private static readonly InstructionOperation[] ShortDisplayOps = [
        InstructionOperation.JO_SHORT,
        InstructionOperation.JNO_SHORT,
        InstructionOperation.JB_SHORT,
        InstructionOperation.JAE_SHORT,
        InstructionOperation.JE_SHORT,
        InstructionOperation.JNE_SHORT,
        InstructionOperation.JBE_SHORT,
        InstructionOperation.JA_SHORT,
        InstructionOperation.JS_SHORT,
        InstructionOperation.JNS_SHORT,
        InstructionOperation.JP_SHORT,
        InstructionOperation.JNP_SHORT,
        InstructionOperation.JL_SHORT,
        InstructionOperation.JGE_SHORT,
        InstructionOperation.JLE_SHORT,
        InstructionOperation.JG_SHORT
    ];

    private static readonly InstructionOperation[] NearDisplayOps = [
        InstructionOperation.JO_NEAR,
        InstructionOperation.JNO_NEAR,
        InstructionOperation.JB_NEAR,
        InstructionOperation.JAE_NEAR,
        InstructionOperation.JE_NEAR,
        InstructionOperation.JNE_NEAR,
        InstructionOperation.JBE_NEAR,
        InstructionOperation.JA_NEAR,
        InstructionOperation.JS_NEAR,
        InstructionOperation.JNS_NEAR,
        InstructionOperation.JP_NEAR,
        InstructionOperation.JNP_NEAR,
        InstructionOperation.JL_NEAR,
        InstructionOperation.JGE_NEAR,
        InstructionOperation.JLE_NEAR,
        InstructionOperation.JG_NEAR
    ];

    public JccParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, int conditionCode) {
        bool is8 = context.OpcodeField.Value <= 0xFF;
        BitWidth offsetWidth = GetBitWidth(is8, context.HasOperandSize32);
        return ParseJcc(context, conditionCode, offsetWidth);
    }

    private CfgInstruction ParseJcc(ParsingContext context, int conditionCode, BitWidth offsetWidth) {
        bool isShort = offsetWidth == BitWidth.BYTE_8;
        InstructionOperation[] displayOps = isShort ? ShortDisplayOps : NearDisplayOps;

        (int offsetValue, FieldWithValue offsetField) = ReadSignedOffset(offsetWidth);

        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(offsetField);
        instr.MaxSuccessorsCount = 2;
        ushort targetIp = (ushort)(instr.NextInMemoryAddress32.Offset + offsetValue);
        ValueNode targetIpNode = _astBuilder.Constant.ToNearAddressNode(targetIp, instr.NextInMemoryAddress32.ToSegmentedAddress());
        ValueNode conditionNode = _astBuilder.Flag.BuildSetCondition(conditionCode);
        InstructionNode displayAst = new InstructionNode(displayOps[conditionCode], targetIpNode);
        IfElseNode execAst = _astBuilder.ControlFlow.ConditionalNearJump(instr, conditionNode, targetIpNode);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}