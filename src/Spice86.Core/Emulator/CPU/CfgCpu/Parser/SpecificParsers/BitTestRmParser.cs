namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>BT/BTS/BTR/BTC RM, R (bit test with register operand)</summary>
public class BitTestRmParser : BaseInstructionParser {
    public BitTestRmParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses a bit test instruction with the specified operation and mutation.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, InstructionOperation displayOp, BitTestMutation mutation) {
        (CfgInstruction instr, DataType dataType, BitWidth bitWidth, ModRmContext modRmContext) = ParseModRm(context, false, 1);

        ValueNode bitIndexNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        BinaryOperationNode bitInElement = new BinaryOperationNode(dataType, bitIndexNode, BinaryOperation.MODULO, _astBuilder.Constant.ToNode(dataType, (ulong)(int)bitWidth));

        ValueNode targetNode;
        bool isMemory = modRmContext.MemoryAddressType != MemoryAddressType.NONE;
        if (!isMemory) {
            targetNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        } else {
            int elementBytes = bitWidth.ToBytes();
            int shiftAmount = bitWidth == BitWidth.DWORD_32 ? 5 : 4;
            ValueNode bitIndexSigned = _astBuilder.TypeConversion.ToSigned(bitIndexNode);
            ValueNode bitIndexInt32 = _astBuilder.TypeConversion.Convert(DataType.INT32, bitIndexSigned);
            BinaryOperationNode elementIndex = new BinaryOperationNode(DataType.INT32, bitIndexInt32, BinaryOperation.RIGHT_SHIFT, _astBuilder.Constant.ToNode(shiftAmount));
            BinaryOperationNode offsetAdjustment = new BinaryOperationNode(DataType.INT32, elementIndex, BinaryOperation.MULTIPLY, _astBuilder.Constant.ToNode(elementBytes));
            targetNode = _astBuilder.ModRm.ToMemoryAddressNodeWithOffsetAdjustment(dataType, modRmContext, offsetAdjustment);
        }

        (IVisitableAstNode setCarry, IVisitableAstNode? assignMutated) = BuildBitTestCarryAndMutation(dataType, targetNode, bitInElement, mutation);

        InstructionNode displayAst = new InstructionNode(displayOp,
            _astBuilder.ModRm.RmToNode(dataType, modRmContext), bitIndexNode);

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
