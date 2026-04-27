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
        ValueNode storageNodeRaw = _astBuilder.InstructionField.ToNode(storageField);
        ValueNode levelNodeRaw = _astBuilder.InstructionField.ToNode(levelField);
        ValueNode maskedLevelValue = new BinaryOperationNode(
            DataType.UINT8, levelNodeRaw, BinaryOperation.BITWISE_AND, _astBuilder.Constant.ToNode((byte)0x1F));
        VariableDeclarationNode levelDeclaration = _astBuilder.DeclareVariable(DataType.UINT8, "level", maskedLevelValue);
        VariableReferenceNode levelReference = levelDeclaration.Reference;
        ValueNode pointerSize = _astBuilder.Constant.ToNode(stackType, (ulong)bitWidth.ToBytes());
        VariableDeclarationNode oldBasePointerDeclaration = _astBuilder.DeclareVariable(
            stackType, "oldBasePointer", _astBuilder.Register.Reg(stackType, RegisterIndex.BpIndex));
        VariableReferenceNode oldBasePointerReference = oldBasePointerDeclaration.Reference;
        VariableDeclarationNode oldStackPointerDeclaration = _astBuilder.DeclareVariable(
            stackType, "oldStackPointer", _astBuilder.Register.Reg(stackType, RegisterIndex.SpIndex));
        VariableReferenceNode oldStackPointerReference = oldStackPointerDeclaration.Reference;
        ValueNode initialSpIndexValue = new BinaryOperationNode(
            stackType, oldStackPointerReference, BinaryOperation.MINUS, pointerSize);
        VariableDeclarationNode spIndexDeclaration = _astBuilder.DeclareVariable(stackType, "spIndex", initialSpIndexValue);
        VariableReferenceNode spIndexReference = spIndexDeclaration.Reference;
        ValueNode ssRegister = _astBuilder.Register.SReg(SegmentRegisterIndex.SsIndex);
        ValueNode spIndexAsWord = _astBuilder.TypeConversion.Convert(DataType.UINT16, spIndexReference);
        ValueNode stackAtSp = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spIndexAsWord);
        BinaryOperationNode pushOldBasePointer = _astBuilder.Assign(stackType, stackAtSp, oldBasePointerReference);
        VariableDeclarationNode framePointerDeclaration = _astBuilder.DeclareVariable(stackType, "framePtr", spIndexReference);
        VariableReferenceNode framePointerReference = framePointerDeclaration.Reference;
        VariableDeclarationNode bpIndexDeclaration = _astBuilder.DeclareVariable(stackType, "bpIndex", oldBasePointerReference);
        VariableReferenceNode bpIndexReference = bpIndexDeclaration.Reference;
        VariableDeclarationNode loopIndexDeclaration = _astBuilder.DeclareVariable(DataType.INT32, "i", _astBuilder.Constant.ToNode(1));
        VariableReferenceNode loopIndexReference = loopIndexDeclaration.Reference;
        ValueNode levelAsInt = _astBuilder.TypeConversion.Convert(DataType.INT32, levelReference);
        ValueNode loopCondition = new BinaryOperationNode(DataType.BOOL, loopIndexReference, BinaryOperation.LESS_THAN, levelAsInt);
        BinaryOperationNode decrementBpIndex = _astBuilder.Assign(stackType, bpIndexReference,
            new BinaryOperationNode(stackType, bpIndexReference, BinaryOperation.MINUS, pointerSize));
        BinaryOperationNode decrementSpIndex = _astBuilder.Assign(stackType, spIndexReference,
            new BinaryOperationNode(stackType, spIndexReference, BinaryOperation.MINUS, pointerSize));
        ValueNode bpIndexAsWord = _astBuilder.TypeConversion.Convert(DataType.UINT16, bpIndexReference);
        ValueNode spLoopIndexAsWord = _astBuilder.TypeConversion.Convert(DataType.UINT16, spIndexReference);
        ValueNode sourcePointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, bpIndexAsWord);
        ValueNode destinationPointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spLoopIndexAsWord);
        BinaryOperationNode copyFrameValue = _astBuilder.Assign(stackType, destinationPointer, sourcePointer);
        BlockNode loopBody = new BlockNode(decrementBpIndex, decrementSpIndex, copyFrameValue);
        BinaryOperationNode incrementLoopIndex = _astBuilder.Assign(DataType.INT32, loopIndexReference,
            _astBuilder.Constant.AddConstant(DataType.INT32, loopIndexReference, 1));
        BlockNode forLoop = _astBuilder.ControlFlow.For(loopIndexDeclaration, loopCondition, incrementLoopIndex, loopBody);
        BinaryOperationNode decrementSpForFramePointer = _astBuilder.Assign(stackType, spIndexReference,
            new BinaryOperationNode(stackType, spIndexReference, BinaryOperation.MINUS, pointerSize));
        ValueNode spAfterLoopAsWord = _astBuilder.TypeConversion.Convert(DataType.UINT16, spIndexReference);
        ValueNode destinationFramePointer = _astBuilder.Pointer.ToSegmentedPointer(stackType, ssRegister, spAfterLoopAsWord);
        BinaryOperationNode pushFramePointer = _astBuilder.Assign(stackType, destinationFramePointer, framePointerReference);
        BlockNode levelNotZeroBlock = new BlockNode(bpIndexDeclaration, forLoop, decrementSpForFramePointer, pushFramePointer);
        ValueNode levelNotZeroCondition = new BinaryOperationNode(DataType.BOOL, levelReference, BinaryOperation.NOT_EQUAL,
            _astBuilder.Constant.ToNode((byte)0));
        IfElseNode handleNestingLevel = _astBuilder.ControlFlow.If(levelNotZeroCondition, levelNotZeroBlock);
        BinaryOperationNode setBasePointerToFrame = _astBuilder.Assign(stackType,
            _astBuilder.Register.Reg(stackType, RegisterIndex.BpIndex), framePointerReference);
        ValueNode storageAsStackType = _astBuilder.TypeConversion.Convert(stackType, storageNodeRaw);
        BinaryOperationNode subtractStorage = _astBuilder.Assign(stackType, spIndexReference,
            new BinaryOperationNode(stackType, spIndexReference, BinaryOperation.MINUS, storageAsStackType));
        BinaryOperationNode setStackPointer = _astBuilder.Assign(stackType,
            _astBuilder.Register.Reg(stackType, RegisterIndex.SpIndex), spIndexReference);
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
