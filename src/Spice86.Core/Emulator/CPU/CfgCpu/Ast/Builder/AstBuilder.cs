namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

public class AstBuilder {
    public AstBuilder() {
        SegmentedAddressBuilder = new(Constant, TypeConversion);
        InstructionField = new(Constant, Pointer, SegmentedAddressBuilder);
        ModRm = new(Register, InstructionField, Pointer, Constant);
        Bitwise = new(Constant);
        ControlFlow = new(Constant);
        Flag = new(ControlFlow);
        Stack = new();
        StringOperation = new(ControlFlow, Pointer, Register, TypeConversion);
        Io = new(TypeConversion);
    }

    public RegisterAstBuilder Register { get; } = new();
    public PointerAstBuilder Pointer { get; } = new();
    public ConstantAstBuilder Constant { get; } = new();
    public FlagAstBuilder Flag { get; }
    public TypeConversionAstBuilder TypeConversion { get; } = new();
    public SegmentedAddressAstBuilder SegmentedAddressBuilder { get; }
    public InstructionFieldAstBuilder InstructionField { get; }
    public ModRmAstBuilder ModRm { get; }
    public BitwiseAstBuilder Bitwise { get; }
    public ControlFlowAstBuilder ControlFlow { get; }
    public StackAstBuilder Stack { get; }
    public StringOperationAstBuilder StringOperation { get; }
    public IoAstBuilder Io { get; }

    public DataType SType(BitWidth bitWidth) {
        return DataType.SignedFromBitWidth(bitWidth);
    }

    public DataType UType(BitWidth bitWidth) {
        return DataType.UnsignedFromBitWidth(bitWidth);
    }

    public DataType AddressType(CfgInstruction instruction) {
        return instruction.AddressSize32Prefix == null ? DataType.UINT16 : DataType.UINT32;
    }

    /// <summary>
    /// Computes the AST <see cref="RepPrefix"/> enum value from a parsed instruction's
    /// prefix and whether the instruction modifies flags.
    /// </summary>
    public RepPrefix? Rep(Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix.RepPrefix? parsedRepPrefix, bool changesFlags) {
        if (parsedRepPrefix is null) {
            return null;
        }
        if (!changesFlags) {
            return RepPrefix.REP;
        }
        if (parsedRepPrefix.ContinueZeroFlagValue) {
            return RepPrefix.REPE;
        }
        return RepPrefix.REPNE;
    }

    /// <summary>
    /// Wraps instruction logic with IP advancement to the next instruction in memory.
    /// Creates a BlockNode containing the instruction logic followed by MoveIpNextNode.
    /// </summary>
    /// <param name="instruction">The instruction being executed (for NextInMemoryAddress)</param>
    /// <param name="statements">Statements for the block</param>
    /// <returns>A BlockNode with instruction logic + IP advancement</returns>
    public IVisitableAstNode WithIpAdvancement(CfgInstruction instruction, params IVisitableAstNode[] statements) {
        List<IVisitableAstNode> allStatements = new();
        allStatements.AddRange(statements);
        allStatements.Add(new MoveIpNextNode(Constant.ToNode(instruction.NextInMemoryAddress32.Offset)));
        return new BlockNode(allStatements.ToArray());
    }


    /// <summary>
    /// Helper to get both R and RM ModRm operands in a single call.
    /// Returns a tuple of (R, RM) nodes for the specified data type and ModRmContext.
    /// </summary>
    /// <param name="dataType">The data type for both operands</param>
    /// <param name="modRmContext">The ModRM context</param>
    /// <returns>Tuple of (R node, RM node)</returns>
    public (ValueNode R, ValueNode Rm) ModRmOperands(DataType dataType, ModRmContext modRmContext) {
        return (ModRm.RToNode(dataType, modRmContext), ModRm.RmToNode(dataType, modRmContext));
    }

    /// <summary>
    /// Creates an assignment node: destination = source.
    /// Delegates to <see cref="ControlFlowAstBuilder.Assign"/>.
    /// </summary>
    /// <param name="dataType">The data type for the assignment</param>
    /// <param name="destination">The destination (left-hand side)</param>
    /// <param name="source">The source value (right-hand side)</param>
    /// <returns>BinaryOperationNode representing the assignment</returns>
    public BinaryOperationNode Assign(DataType dataType, ValueNode destination, ValueNode source) {
        return ControlFlow.Assign(dataType, destination, source);
    }

    /// <summary>
    /// Creates a variable declaration node with an initializer.
    /// Example: "ushort result = Alu8.Mul(AL, RM8);"
    /// </summary>
    /// <param name="dataType">The data type of the variable</param>
    /// <param name="variableName">The name of the variable</param>
    /// <param name="initializer">The expression that initializes the variable</param>
    /// <returns>VariableDeclarationNode representing the declaration and initialization</returns>
    public VariableDeclarationNode DeclareVariable(DataType dataType, string variableName, ValueNode initializer) {
        return new VariableDeclarationNode(dataType, variableName, initializer);
    }

    /// <summary>
    /// Conditionally wraps a value in an assignment if assign is true, otherwise returns the value.
    /// Useful for instructions that optionally assign results (e.g., CMP vs ADD).
    /// </summary>
    /// <param name="dataType">The data type</param>
    /// <param name="destination">The destination node (only used if assign is true)</param>
    /// <param name="source">The source value</param>
    /// <param name="assign">Whether to create an assignment</param>
    /// <returns>Assignment node if assign is true, otherwise the source node</returns>
    public IVisitableAstNode ConditionalAssign(DataType dataType, ValueNode destination, ValueNode source, bool assign) {
        return assign ? Assign(dataType, destination, source) : source;
    }

    /// <summary>
    /// Sign-extends a value from a smaller type to a larger signed type.
    /// Example: UINT8 -> INT8 -> INT16 (keeps result as signed)
    /// </summary>
    /// <param name="value">The value to sign-extend</param>
    /// <param name="fromBitWidth">Source bit width</param>
    /// <param name="toBitWidth">Destination bit width</param>
    /// <returns>Sign-extended value as signed type</returns>
    public ValueNode SignExtend(ValueNode value, BitWidth fromBitWidth, BitWidth toBitWidth) {
        // Convert to signed source type, then to signed dest type
        TypeConversionNode toSignedSource = new TypeConversionNode(SType(fromBitWidth), value);
        TypeConversionNode toSignedDest = new TypeConversionNode(SType(toBitWidth), toSignedSource);
        return toSignedDest;
    }

    /// <summary>
    /// Sign-extends a value from a smaller type to a larger unsigned type.
    /// Example: UINT8 -> INT8 -> INT16 -> UINT16 (for CBW, CWDE instructions)
    /// Useful for instructions that need sign extension but store result in unsigned register.
    /// </summary>
    /// <param name="value">The value to sign-extend</param>
    /// <param name="fromBitWidth">Source bit width</param>
    /// <param name="toBitWidth">Destination bit width</param>
    /// <returns>Sign-extended value as unsigned type</returns>
    public ValueNode SignExtendToUnsigned(ValueNode value, BitWidth fromBitWidth, BitWidth toBitWidth) {
        // Convert to signed source type, then to signed dest type, then to unsigned dest type
        ValueNode signExtended = SignExtend(value, fromBitWidth, toBitWidth);
        return TypeConversion.Convert(UType(toBitWidth), signExtended);
    }

    /// <summary>
    /// Creates an ALU method call value node with variable number of operands.
    /// Examples:
    ///   - "Alu8.Inc(value)" - 1 operand
    ///   - "Alu8.Add(v1, v2)" - 2 operands
    ///   - "Alu16.Shld(rm, r, count)" - 3 operands
    /// </summary>
    /// <param name="resultType">The data type of the result</param>
    /// <param name="bitWidth">The ALU bit width</param>
    /// <param name="operation">The operation name (e.g., "Add", "Sub", "Inc", "Shld")</param>
    /// <param name="operands">Variable number of operands</param>
    /// <returns>MethodCallValueNode representing the ALU operation</returns>
    public MethodCallValueNode AluCall(DataType resultType, BitWidth bitWidth, string operation, params ValueNode[] operands) {
        return new MethodCallValueNode(resultType, $"Alu{(int)bitWidth}", operation, operands);
    }

    /// <summary>
    /// Creates an ALU method call and declares a variable to hold the result.
    /// Example: "uint result = Alu8.Mul(v1, v2);"
    /// </summary>
    /// <param name="resultType">The data type of the result</param>
    /// <param name="bitWidth">The ALU bit width</param>
    /// <param name="operation">The operation name (e.g., "Mul", "Div")</param>
    /// <param name="variableName">Optional variable name (defaults to "result")</param>
    /// <param name="operands">Variable number of operands</param>
    /// <returns>VariableDeclarationNode with the ALU call as initializer</returns>
    public VariableDeclarationNode DeclareAluResult(DataType resultType, BitWidth bitWidth, string operation, string variableName, params ValueNode[] operands) {
        MethodCallValueNode aluCall = AluCall(resultType, bitWidth, operation, operands);
        return DeclareVariable(resultType, variableName, aluCall);
    }

    /// <summary>
    /// Assigns a value to a destination with automatic type conversion.
    /// Example: "AX = (ushort)result" where result is a different type.
    /// </summary>
    /// <param name="targetType">The target type for the assignment</param>
    /// <param name="destination">The destination node</param>
    /// <param name="source">The source value (will be converted if needed)</param>
    /// <returns>Assignment node with type conversion if needed</returns>
    public IVisitableAstNode AssignWithConversion(DataType targetType, ValueNode destination, ValueNode source) {
        ValueNode converted = TypeConversion.Convert(targetType, source);
        return Assign(targetType, destination, converted);
    }

    /// <summary>
    /// Combines high and low registers into a wide value using shift and OR.
    /// Example: For 16-bit: (DX &lt;&lt; 16) | AX creates a 32-bit value.
    /// Result is returned as signed type for use in division operations.
    /// </summary>
    /// <param name="highReg">The high register node (e.g., DX or EDX)</param>
    /// <param name="lowReg">The low register node (e.g., AX or EAX)</param>
    /// <param name="bitWidth">Bit width of individual registers</param>
    /// <param name="resultType">The result type (signed wide type)</param>
    /// <returns>Combined value as signed wide type</returns>
    public ValueNode CombineHighLowRegisters(ValueNode highReg, ValueNode lowReg, BitWidth bitWidth, DataType resultType) {
        DataType wideUnsignedType = UType(bitWidth.Double());

        // Convert to wide unsigned for bit operations
        ValueNode highWide = TypeConversion.Convert(wideUnsignedType, highReg);
        ValueNode lowWide = TypeConversion.Convert(wideUnsignedType, lowReg);

        // Shift high left by size bits
        BinaryOperationNode shiftedHigh = new BinaryOperationNode(wideUnsignedType, highWide, BinaryOperation.LEFT_SHIFT, Constant.ToNode((int)bitWidth));

        // OR with low part
        BinaryOperationNode combined = new BinaryOperationNode(wideUnsignedType, shiftedHigh, BinaryOperation.BITWISE_OR, lowWide);

        // Convert to signed for the operation
        return TypeConversion.Convert(resultType, combined);
    }

    /// <summary>
    /// Extracts the upper bits of a wide result by shifting right.
    /// Example: For multiplication, extracts the upper 16 bits from a 32-bit result.
    /// </summary>
    /// <param name="source">The source variable reference</param>
    /// <param name="bitWidth">Bit width to shift right by</param>
    /// <param name="targetType">Target type for the extracted value</param>
    /// <returns>Upper bits converted to target type</returns>
    public ValueNode ExtractUpperBits(VariableReferenceNode source, BitWidth bitWidth, DataType targetType) {
        BinaryOperationNode shiftRight = new BinaryOperationNode(source.DataType, source, BinaryOperation.RIGHT_SHIFT, Constant.ToNode((int)bitWidth));
        return TypeConversion.Convert(targetType, shiftRight);
    }

    /// <summary>
    /// Extracts the lower bits of a wide result with type conversion.
    /// Example: For multiplication, extracts the lower 16 bits from a 32-bit result.
    /// </summary>
    /// <param name="source">The source variable reference</param>
    /// <param name="targetType">Target type for the extracted value</param>
    /// <returns>Lower bits converted to target type</returns>
    public ValueNode ExtractLowerBits(VariableReferenceNode source, DataType targetType) {
        return TypeConversion.Convert(targetType, source);
    }

}