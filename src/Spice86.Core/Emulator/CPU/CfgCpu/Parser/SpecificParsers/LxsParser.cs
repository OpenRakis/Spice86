namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>LES/LDS/LSS/LFS/LGS (load far pointer)</summary>
public class LxsParser : BaseInstructionParser {
    public LxsParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses a load far pointer instruction with the specified parameters.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, InstructionOperation displayOp, SegmentRegisterIndex segmentRegisterIndex) {
        (CfgInstruction instr, DataType dataType, BitWidth bitWidth, ModRmContext modRmContext) = ParseModRm(context, false, 1);
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            throw new CpuInvalidOpcodeException($"{displayOp} with register source operand is invalid");
        }

        DataType addrType = _astBuilder.AddressType(instr);
        (VariableDeclarationNode cachedOffset, ValueNode memValue) =
            _astBuilder.ModRm.ToMemoryAddressNodeWithCachedOffset(dataType, addrType, modRmContext, "lxsOffset");

        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        VariableDeclarationNode offsetValue = _astBuilder.DeclareVariable(dataType, "lxsValue", memValue);
        BinaryOperationNode assignR = _astBuilder.Assign(dataType, rNode, offsetValue.Reference);

        ValueNode sizeInBytes = addrType == DataType.UINT16
            ? _astBuilder.Constant.ToNode((ushort)bitWidth.ToBytes())
            : _astBuilder.Constant.ToNode((uint)bitWidth.ToBytes());
        ValueNode adjustedOffset = new BinaryOperationNode(addrType, cachedOffset.Reference, BinaryOperation.PLUS, sizeInBytes);
        ValueNode segPointer = _astBuilder.ModRm.ToMemoryAddressNodeWithCustomOffset(DataType.UINT16, modRmContext, adjustedOffset);
        VariableDeclarationNode segmentValue = _astBuilder.DeclareVariable(DataType.UINT16, "lxsSegment", segPointer);
        ValueNode segRegNode = _astBuilder.Register.SReg(segmentRegisterIndex);
        BinaryOperationNode assignSeg = _astBuilder.Assign(DataType.UINT16, segRegNode, segmentValue.Reference);

        InstructionNode displayAst = new InstructionNode(displayOp, rNode, _astBuilder.ModRm.RmToNode(dataType, modRmContext));

        IVisitableAstNode execAst;
        if (segmentRegisterIndex == SegmentRegisterIndex.SsIndex) {
            IVisitableAstNode setInterruptShadowingNode = _astBuilder.Flag.SetInterruptShadowing();
            execAst = _astBuilder.WithIpAdvancement(instr, cachedOffset, offsetValue, segmentValue, assignR, assignSeg, setInterruptShadowingNode);
        } else {
            execAst = _astBuilder.WithIpAdvancement(instr, cachedOffset, offsetValue, segmentValue, assignR, assignSeg);
        }
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
