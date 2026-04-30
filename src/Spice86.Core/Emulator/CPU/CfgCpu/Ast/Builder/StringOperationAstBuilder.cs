namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Builder for creating string operation AST nodes, handling REP prefix logic.
/// </summary>
public class StringOperationAstBuilder {
    private readonly ControlFlowAstBuilder _controlFlow;
    private readonly PointerAstBuilder _pointer;
    private readonly RegisterAstBuilder _register;
    private readonly TypeConversionAstBuilder _typeConversion;

    public StringOperationAstBuilder(ControlFlowAstBuilder controlFlow, PointerAstBuilder pointer, RegisterAstBuilder register, TypeConversionAstBuilder typeConversion) {
        _controlFlow = controlFlow;
        _pointer = pointer;
        _register = register;
        _typeConversion = typeConversion;
    }

    /// <summary>
    /// Generates the execution AST for a flat <see cref="CfgInstruction"/> string operation,
    /// including REP prefix handling.  Used by parsers that build the core operation inline.
    /// </summary>
    public IVisitableAstNode GenerateExecutionAst(CfgInstruction instruction, bool changesFlags,
        BlockNode coreOperation, AstBuilder builder) {
        RepPrefix? repPrefix = builder.Rep(instruction.RepPrefix, changesFlags);

        if (repPrefix == null) {
            // No REP prefix, execute once
            return builder.WithIpAdvancement(instruction, coreOperation);
        }
        // REP prefix, create while loop
        ValueNode cxCondition = new BinaryOperationNode(
            DataType.BOOL,
            builder.Register.Reg16(RegisterIndex.CxIndex),
            BinaryOperation.NOT_EQUAL,
            builder.Constant.ToNode((ushort)0));

        // Decrement CX
        BinaryOperationNode decrementCx = builder.Assign(
            DataType.UINT16,
            builder.Register.Reg16(RegisterIndex.CxIndex),
            builder.Constant.AddConstant(builder.Register.Reg16(RegisterIndex.CxIndex), -1));

        // Check if we need flag-based continuation for REPE/REPNE
        if (repPrefix != RepPrefix.REP) {
            // For REPE/REPNE: the flag condition is checked AFTER each iteration, not before.
            // Structure: while (CX != 0 && shouldContinue) { op; CX--; shouldContinue = flagCondition; }
            ValueNode flagCondition = CreateFlagCondition(builder, repPrefix.Value);

            VariableDeclarationNode shouldContinue = builder.DeclareVariable(DataType.BOOL, "shouldContinue", builder.Constant.ToNode(true));

            ValueNode combinedCondition = new BinaryOperationNode(
                DataType.BOOL,
                cxCondition,
                BinaryOperation.LOGICAL_AND,
                shouldContinue.Reference);

            BinaryOperationNode updateContinue = builder.Assign(
                DataType.BOOL, shouldContinue.Reference, flagCondition);

            BlockNode loopBody = new BlockNode(coreOperation, decrementCx, updateContinue);
            WhileNode whileLoop = builder.ControlFlow.While(combinedCondition, loopBody);
            return builder.WithIpAdvancement(instruction, shouldContinue, whileLoop);
        } else {
            // For REP or instructions that don't change flags, no flag check needed
            BlockNode loopBody = new BlockNode(coreOperation, decrementCx);
            WhileNode whileLoop = builder.ControlFlow.While(cxCondition, loopBody);
            return builder.WithIpAdvancement(instruction, whileLoop);
        }
    }

    /// <summary>
    /// Creates a segmented pointer to DS:SI (with optional segment override) for string source operand.
    /// </summary>
    public ValueNode SourcePointerSi(DataType dataType, DataType addressType, int segmentRegisterIndex, int? defaultSegmentRegisterIndex) {
        return _pointer.ToSegmentedPointer(
            dataType,
            segmentRegisterIndex,
            defaultSegmentRegisterIndex,
            _register.Reg(addressType, RegisterIndex.SiIndex));
    }

    /// <summary>
    /// Creates a segmented pointer to ES:DI for string destination operand.
    /// </summary>
    public ValueNode DestPointerDi(DataType dataType, DataType addressType) {
        return _pointer.ToSegmentedPointer(
            dataType,
            SegmentRegisterIndex.EsIndex,
            _register.Reg(addressType, RegisterIndex.DiIndex));
    }

    /// <summary>
    /// Creates an assignment that advances SI by the direction step for the given data size.
    /// </summary>
    public BinaryOperationNode AdvanceSi(DataType addressType, int dataSize) {
        ValueNode direction = _typeConversion.Convert(addressType, DirectionNode(dataSize));
        return _controlFlow.Assign(
            addressType,
            _register.Reg(addressType, RegisterIndex.SiIndex),
            new BinaryOperationNode(
                addressType,
                _register.Reg(addressType, RegisterIndex.SiIndex),
                BinaryOperation.PLUS,
                direction));
    }

    /// <summary>
    /// Creates an assignment that advances DI by the direction step for the given data size.
    /// </summary>
    public BinaryOperationNode AdvanceDi(DataType addressType, int dataSize) {
        ValueNode direction = _typeConversion.Convert(addressType, DirectionNode(dataSize));
        return _controlFlow.Assign(
            addressType,
            _register.Reg(addressType, RegisterIndex.DiIndex),
            new BinaryOperationNode(
                addressType,
                _register.Reg(addressType, RegisterIndex.DiIndex),
                BinaryOperation.PLUS,
                direction));
    }

    private ValueNode CreateFlagCondition(AstBuilder builder, Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.RepPrefix repPrefix) {
        // For REPE, continue while ZF == true (ContinueZeroFlagValue = true)
        // For REPNE, continue while ZF == false (ContinueZeroFlagValue = false)
        bool expectedZeroFlag = repPrefix == Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.RepPrefix.REPE;

        return new BinaryOperationNode(DataType.BOOL, builder.Flag.Zero(), BinaryOperation.EQUAL, builder.Constant.ToNode(expectedZeroFlag));
    }

    private static MethodCallValueNode DirectionNode(int dataSize) {
        return new MethodCallValueNode(DataType.INT16, "State", $"Direction{dataSize}");
    }
}