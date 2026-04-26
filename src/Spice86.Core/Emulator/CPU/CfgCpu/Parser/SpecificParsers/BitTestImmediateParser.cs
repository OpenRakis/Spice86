namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for bit test with immediate instructions (opcode 0F BA /4-7):
/// BT, BTS, BTR, BTC with ModRM + immediate byte.
/// </summary>
public class BitTestImmediateParser : BaseGrpOperationParser {
    private static readonly (InstructionOperation DisplayOp, BitTestMutation Mutation)[] BitTestOps = {
        (InstructionOperation.BT, BitTestMutation.None),
        (InstructionOperation.BTS, BitTestMutation.Set),
        (InstructionOperation.BTR, BitTestMutation.Reset),
        (InstructionOperation.BTC, BitTestMutation.Toggle),
    };

    public BitTestImmediateParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        if (groupIndex < 4 || groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        (InstructionOperation displayOp, BitTestMutation mutation) = BitTestOps[groupIndex - 4];
        InstructionField<byte> immField = _instructionReader.UInt8.NextField(false);
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        instr.AddField(immField);
        ValueNode immNode = _astBuilder.InstructionField.ToNode(immField);
        ValueNode bitIndexNode = _astBuilder.TypeConversion.Convert(dataType, immNode);
        BinaryOperationNode bitInElement = new BinaryOperationNode(dataType, bitIndexNode,
            BinaryOperation.MODULO, _astBuilder.Constant.ToNode(dataType, (ulong)(int)bitWidth));
        ValueNode targetNode;
        bool isMemory = modRmContext.MemoryAddressType != MemoryAddressType.NONE;
        if (!isMemory) {
            targetNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        } else {
            uint elementSize = (uint)(int)bitWidth;
            uint elementBytes = (uint)bitWidth.ToBytes();
            ValueNode bitIndexUInt32 = _astBuilder.TypeConversion.Convert(DataType.UINT32, bitIndexNode);
            BinaryOperationNode elementIndex = new BinaryOperationNode(DataType.UINT32, bitIndexUInt32,
                BinaryOperation.DIVIDE, _astBuilder.Constant.ToNode(elementSize));
            BinaryOperationNode offsetAdjustment = new BinaryOperationNode(DataType.UINT32, elementIndex,
                BinaryOperation.MULTIPLY, _astBuilder.Constant.ToNode(elementBytes));
            targetNode = _astBuilder.ModRm.ToMemoryAddressNodeWithOffsetAdjustment(dataType, modRmContext,
                offsetAdjustment);
        }
        (IVisitableAstNode setCarry, IVisitableAstNode? assignMutated) = BuildBitTestCarryAndMutation(dataType, targetNode, bitInElement, mutation);
        InstructionNode displayAst = new InstructionNode(displayOp,
            _astBuilder.ModRm.RmToNode(dataType, modRmContext),
            immNode);
        if (assignMutated == null) {
            IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, setCarry);
            instr.AttachAsts(displayAst, execAst);
        } else {
            IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, setCarry, assignMutated);
            instr.AttachAsts(displayAst, execAst);
        }
        return instr;
    }
}
