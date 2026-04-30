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

public class LoopParser(ParsingTools parsingTools) : BaseInstructionParser(parsingTools) {
    /// <summary>LOOPNE/LOOPNZ (0xE0): loop while counter != 0 AND ZF == 0</summary>
    public CfgInstruction ParseLoopne(ParsingContext context) {
        return ParseLoop(context, InstructionOperation.LOOPNE, LoopVariant.Loopne);
    }

    /// <summary>LOOPE/LOOPZ (0xE1): loop while counter != 0 AND ZF == 1</summary>
    public CfgInstruction ParseLoope(ParsingContext context) {
        return ParseLoop(context, InstructionOperation.LOOPE, LoopVariant.Loope);
    }

    /// <summary>LOOP (0xE2): loop while counter != 0</summary>
    public CfgInstruction ParseLoop(ParsingContext context) {
        return ParseLoop(context, InstructionOperation.LOOP, LoopVariant.Loop);
    }

    private CfgInstruction ParseLoop(ParsingContext context, InstructionOperation displayOp, LoopVariant variant) {
        BitWidth addressWidth = context.AddressWidthFromPrefixes;
        (int offsetValue, FieldWithValue offsetField) = ReadSignedOffset(BitWidth.BYTE_8);

        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(offsetField);
        instr.MaxSuccessorsCount = 2;

        DataType counterType = _astBuilder.UType(addressWidth);
        ValueNode counter = _astBuilder.Register.Reg(counterType, RegisterIndex.CxIndex);

        ushort targetIp = (ushort)(instr.NextInMemoryAddress32.Offset + offsetValue);
        ValueNode targetIpNode = _astBuilder.Constant.ToNearAddressNode(targetIp, instr.NextInMemoryAddress32.ToSegmentedAddress());

        BinaryOperationNode decrementCounter = _astBuilder.Assign(
            counter.DataType,
            counter,
            _astBuilder.Constant.AddConstant(counter, -1));

        ValueNode counterNotZero = new BinaryOperationNode(
            DataType.BOOL,
            counter,
            BinaryOperation.NOT_EQUAL,
            _astBuilder.Constant.ToNode(counter.DataType, 0UL));

        ValueNode condition = variant switch {
            LoopVariant.Loop => counterNotZero,
            LoopVariant.Loope => new BinaryOperationNode(DataType.BOOL, counterNotZero, BinaryOperation.LOGICAL_AND, _astBuilder.Flag.Zero()),
            LoopVariant.Loopne => new BinaryOperationNode(
                DataType.BOOL,
                counterNotZero,
                BinaryOperation.LOGICAL_AND,
                new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, _astBuilder.Flag.Zero())),
            _ => throw new InvalidOperationException($"Unknown loop variant: {variant}")
        };

        IfElseNode conditionalJump = _astBuilder.ControlFlow.ConditionalNearJump(instr, condition, targetIpNode);
        InstructionNode displayAst = new InstructionNode(displayOp, targetIpNode);
        BlockNode execAst = new BlockNode(decrementCounter, conditionalJump);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private enum LoopVariant {
        Loop,
        Loope,
        Loopne,
    }
}
