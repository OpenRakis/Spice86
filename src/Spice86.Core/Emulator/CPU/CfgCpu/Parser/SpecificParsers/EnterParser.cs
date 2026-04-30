namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>ENTER</summary>
public class EnterParser : BaseInstructionParser {
    public EnterParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        InstructionField<ushort> storageField = _instructionReader.UInt16.NextField(false);
        InstructionField<byte> levelField = _instructionReader.UInt8.NextField(false);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(storageField);
        instr.AddField(levelField);
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        DataType stackType = _astBuilder.UType(bitWidth);
        // SP/BP arithmetic always uses the 16-bit stack pointer in real mode,
        // even with the 0x66 (operand-size 32) prefix. Only the push/copy
        // widths and the destination register width depend on bitWidth, so
        // ESP[31:16] survives the instruction unchanged.
        DataType spType = _astBuilder.UType(BitWidth.WORD_16);
        ValueNode storageNodeRaw = _astBuilder.InstructionField.ToNode(storageField);
        ValueNode levelNodeRaw = _astBuilder.InstructionField.ToNode(levelField);
        ValueNode maskedLevelValue = new BinaryOperationNode(
            DataType.UINT8, levelNodeRaw, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode((byte)0x1F));
        VariableDeclarationNode levelDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "level", maskedLevelValue);
        VariableReferenceNode levelReference = levelDeclaration.Reference;
        ValueNode pointerSize = _astBuilder.Constant.ToNode(spType, (ulong)bitWidth.ToBytes());
        VariableDeclarationNode oldBasePointerDeclaration = _astBuilder.DeclareVariable(
            stackType, "oldBasePointer", _astBuilder.Register.Reg(stackType, RegisterIndex.BpIndex));
        VariableReferenceNode oldBasePointerReference = oldBasePointerDeclaration.Reference;
        VariableDeclarationNode oldStackPointerDeclaration = _astBuilder.DeclareVariable(
            spType, "oldStackPointer", _astBuilder.Register.Reg(spType, RegisterIndex.SpIndex));
        VariableReferenceNode oldStackPointerReference = oldStackPointerDeclaration.Reference;
        ValueNode initialSpIndexValue = new BinaryOperationNode(
            spType, oldStackPointerReference, BinaryOperation.MINUS, pointerSize);
        VariableDeclarationNode spIndexDeclaration = _astBuilder.DeclareVariable(spType, "spIndex", initialSpIndexValue);
        VariableReferenceNode spIndexReference = spIndexDeclaration.Reference;
        ValueNode ssRegister = _astBuilder.Register.SReg(SegmentRegisterIndex.SsIndex);
        ValueNode stackAtSp = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spIndexReference);
        BinaryOperationNode pushOldBasePointer = _astBuilder.Assign(stackType, stackAtSp, oldBasePointerReference);
        VariableDeclarationNode framePointerDeclaration = _astBuilder.DeclareVariable(spType, "framePtr", spIndexReference);
        VariableReferenceNode framePointerReference = framePointerDeclaration.Reference;
        ValueNode oldBasePointerAsWord = _astBuilder.TypeConversion.Convert(spType, oldBasePointerReference);
        VariableDeclarationNode bpIndexDeclaration = _astBuilder.DeclareVariable(spType, "bpIndex", oldBasePointerAsWord);
        VariableReferenceNode bpIndexReference = bpIndexDeclaration.Reference;
        VariableDeclarationNode loopIndexDeclaration = _astBuilder.DeclareVariable(DataType.INT32, "i", _astBuilder.Constant.ToNode(1));
        VariableReferenceNode loopIndexReference = loopIndexDeclaration.Reference;
        ValueNode levelAsInt = _astBuilder.TypeConversion.Convert(DataType.INT32, levelReference);
        ValueNode loopCondition = new BinaryOperationNode(DataType.BOOL, loopIndexReference, BinaryOperation.LESS_THAN, levelAsInt);
        BinaryOperationNode decrementBpIndex = _astBuilder.Assign(spType, bpIndexReference,
            new BinaryOperationNode(spType, bpIndexReference, BinaryOperation.MINUS, pointerSize));
        BinaryOperationNode decrementSpIndex = _astBuilder.Assign(spType, spIndexReference,
            new BinaryOperationNode(spType, spIndexReference, BinaryOperation.MINUS, pointerSize));
        ValueNode sourcePointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, bpIndexReference);
        ValueNode destinationPointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spIndexReference);
        BinaryOperationNode copyFrameValue = _astBuilder.Assign(stackType, destinationPointer, sourcePointer);
        BlockNode loopBody = new BlockNode(decrementBpIndex, decrementSpIndex, copyFrameValue);
        BinaryOperationNode incrementLoopIndex = _astBuilder.Assign(DataType.INT32, loopIndexReference,
            _astBuilder.Constant.AddConstant(DataType.INT32, loopIndexReference, 1));
        BlockNode forLoop = _astBuilder.ControlFlow.For(loopIndexDeclaration, loopCondition, incrementLoopIndex, loopBody);
        BinaryOperationNode decrementSpForFramePointer = _astBuilder.Assign(spType, spIndexReference,
            new BinaryOperationNode(spType, spIndexReference, BinaryOperation.MINUS, pointerSize));
        ValueNode destinationFramePointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spIndexReference);
        ValueNode framePointerAsStackType = _astBuilder.TypeConversion.Convert(stackType, framePointerReference);
        BinaryOperationNode pushFramePointer = _astBuilder.Assign(stackType, destinationFramePointer, framePointerAsStackType);
        BlockNode levelNotZeroBlock = new BlockNode(bpIndexDeclaration, forLoop, decrementSpForFramePointer, pushFramePointer);
        ValueNode levelNotZeroCondition = new BinaryOperationNode(DataType.BOOL, levelReference, BinaryOperation.NOT_EQUAL,
            _astBuilder.Constant.ToNode((byte)0));
        IfElseNode handleNestingLevel = _astBuilder.ControlFlow.If(levelNotZeroCondition, levelNotZeroBlock);
        ValueNode framePointerForBp = _astBuilder.TypeConversion.Convert(stackType, framePointerReference);
        BinaryOperationNode setBasePointerToFrame = _astBuilder.Assign(stackType,
            _astBuilder.Register.Reg(stackType, RegisterIndex.BpIndex), framePointerForBp);
        BinaryOperationNode subtractStorage = _astBuilder.Assign(spType, spIndexReference,
            new BinaryOperationNode(spType, spIndexReference, BinaryOperation.MINUS, storageNodeRaw));
        BinaryOperationNode setStackPointer = _astBuilder.Assign(spType,
            _astBuilder.Register.Reg(spType, RegisterIndex.SpIndex), spIndexReference);
        InstructionOperation enterOp = bitWidth == BitWidth.DWORD_32 ? InstructionOperation.ENTERW : InstructionOperation.ENTER;
        InstructionNode displayAst = new InstructionNode(enterOp, storageNodeRaw, levelNodeRaw);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            levelDeclaration, oldBasePointerDeclaration, oldStackPointerDeclaration, spIndexDeclaration,
            pushOldBasePointer, framePointerDeclaration, handleNestingLevel,
            setBasePointerToFrame, subtractStorage, setStackPointer);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
