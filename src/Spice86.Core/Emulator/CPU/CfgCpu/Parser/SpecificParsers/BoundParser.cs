namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>BOUND</summary>
public class BoundParser : BaseInstructionParser {
    public BoundParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, 1);
        _modRmParser.EnsureNotMode3(modRmContext);
        BitWidth bitWidth = context.DefaultWordOperandBitWidth;
        int elementSize = bitWidth.ToBytes();
        DataType signedType = _astBuilder.SType(bitWidth);
        DataType addrType = _astBuilder.UType(BitWidth.WORD_16);

        ValueNode indexNode = _astBuilder.TypeConversion.Convert(signedType,
            _astBuilder.ModRm.RToNode(_astBuilder.UType(bitWidth), modRmContext));

        ValueNode lowerPtr = _astBuilder.ModRm.ToMemoryAddressNode(signedType, modRmContext);

        ValueNode sizeNode = addrType == DataType.UINT16
            ? _astBuilder.Constant.ToNode((ushort)elementSize)
            : _astBuilder.Constant.ToNode((uint)elementSize);
        ValueNode upperPtr = _astBuilder.ModRm.ToMemoryAddressNodeWithOffsetAdjustment(signedType, modRmContext, sizeNode);

        if (modRmContext.SegmentIndex is null) {
            throw new InvalidOperationException("Segment index must be set for BOUND memory operand.");
        }
        ValueNode displayIndexNode = _astBuilder.ModRm.RToNode(signedType, modRmContext);
        ValueNode displayLowerPointer = _astBuilder.ModRm.ToMemoryAddressNode(signedType, modRmContext);
        ValueNode offset = _astBuilder.ModRm.MemoryOffsetToNode(modRmContext);
        ValueNode upperOffset = _astBuilder.Constant.AddConstant(_astBuilder.AddressType(instr), offset, elementSize);
        ValueNode displayUpperPointer = _astBuilder.Pointer.ToSegmentedPointer(signedType, (SegmentRegisterIndex)modRmContext.SegmentIndex.Value, upperOffset);
        InstructionNode displayAst = new InstructionNode(
            InstructionOperation.BOUND,
            displayIndexNode,
            displayLowerPointer,
            displayUpperPointer);

        MethodCallNode checkBound = new MethodCallNode(null, "CheckBound", indexNode, lowerPtr, upperPtr);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, checkBound);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
