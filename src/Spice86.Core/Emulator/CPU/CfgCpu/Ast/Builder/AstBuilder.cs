namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class AstBuilder {
    public AstBuilder() {
        InstructionField = new(Constant, Pointer);
        ModRm = new(Register, InstructionField, Pointer);
        Bitwise = new(Constant);
    }

    public RegisterAstBuilder Register { get; } = new();
    public PointerAstBuilder Pointer { get; } = new();
    public ConstantAstBuilder Constant { get; } = new();
    public FlagAstBuilder Flag { get; } = new();
    public TypeConversionAstBuilder TypeConversion { get; } = new();
    public InstructionFieldAstBuilder InstructionField { get; }
    public ModRmAstBuilder ModRm { get; }
    public BitwiseAstBuilder Bitwise { get; }

    public DataType SType(int size) {
        return Type(size, true);
    }
    public DataType UType(int size) {
        return Type(size, false);
    }

    private DataType Type(int size, bool isSigned) {
        return size switch {
            4 => isSigned ? DataType.INT4 : DataType.UINT4,
            5 => isSigned ? DataType.INT5 : DataType.UINT5,
            8 => isSigned ? DataType.INT8 : DataType.UINT8,
            16 => isSigned ? DataType.INT16 : DataType.UINT16,
            32 => isSigned ? DataType.INT32 : DataType.UINT32,
            64 => isSigned ? DataType.INT64 : DataType.UINT64,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "value not handled")
        };
    }

    /// <summary>
    /// Parses a C# type name string and returns the corresponding DataType.
    /// Supports: byte, sbyte, ushort, short, uint, int, ulong, long
    /// </summary>
    /// <param name="csharpTypeName">The C# type name (e.g., "byte", "int", "ulong")</param>
    /// <returns>The corresponding DataType</returns>
    /// <exception cref="ArgumentException">Thrown when the type name is not recognized</exception>
    public DataType ParseCSharpType(string csharpTypeName) {
        return csharpTypeName switch {
            "byte" => DataType.UINT8,
            "sbyte" => DataType.INT8,
            "ushort" => DataType.UINT16,
            "short" => DataType.INT16,
            "uint" => DataType.UINT32,
            "int" => DataType.INT32,
            "ulong" => DataType.UINT64,
            "long" => DataType.INT64,
            _ => throw new ArgumentException($"Unknown C# type name: {csharpTypeName}", nameof(csharpTypeName))
        };
    }

    public DataType AddressType(CfgInstruction instruction) {
        return instruction.AddressSize32Prefix == null ? DataType.UINT16 : DataType.UINT32;
    }

    public RepPrefix? Rep(StringInstruction instruction) {
        if (instruction.RepPrefix is null) {
            return null;
        }
        if (!instruction.ChangesFlags) {
            return RepPrefix.REP;
        }
        if (instruction.RepPrefix.ContinueZeroFlagValue) {
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
        ValueNode nextIpOffset = Constant.ToNode(instruction.NextInMemoryAddress.Offset);
        MoveIpNextNode moveIp = new MoveIpNextNode(nextIpOffset);
        List<IVisitableAstNode> allStatements = new();
        allStatements.AddRange(statements);
        allStatements.Add(moveIp);
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
    /// Creates an assignment node: destination = source
    /// </summary>
    /// <param name="dataType">The data type for the assignment</param>
    /// <param name="destination">The destination (left-hand side)</param>
    /// <param name="source">The source value (right-hand side)</param>
    /// <returns>BinaryOperationNode representing the assignment</returns>
    public BinaryOperationNode Assign(DataType dataType, ValueNode destination, ValueNode source) {
        return new BinaryOperationNode(dataType, destination, BinaryOperation.ASSIGN, source);
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
    /// Creates a reference to a previously declared variable.
    /// </summary>
    /// <param name="dataType">The data type of the variable</param>
    /// <param name="variableName">The name of the variable to reference</param>
    /// <returns>VariableReferenceNode representing the variable reference</returns>
    public VariableReferenceNode VariableReference(DataType dataType, string variableName) {
        return new VariableReferenceNode(dataType, variableName);
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
        return assign ? Assign(dataType, destination, 
            source) : source;
    }

    /// <summary>
    /// Sign-extends a value from a smaller type to a larger signed type.
    /// Example: UINT8 -> INT8 -> INT16 (keeps result as signed)
    /// </summary>
    /// <param name="value">The value to sign-extend</param>
    /// <param name="fromSize">Source size in bits (8, 16, 32)</param>
    /// <param name="toSize">Destination size in bits (16, 32)</param>
    /// <returns>Sign-extended value as signed type</returns>
    public ValueNode SignExtend(ValueNode value, int fromSize, int toSize) {
        // Convert to signed source type, then to signed dest type
        TypeConversionNode toSignedSource = new TypeConversionNode(SType(fromSize), value);
        TypeConversionNode toSignedDest = new TypeConversionNode(SType(toSize), toSignedSource);
        return toSignedDest;
    }

    /// <summary>
    /// Sign-extends a value from a smaller type to a larger unsigned type.
    /// Example: UINT8 -> INT8 -> INT16 -> UINT16 (for CBW, CWDE instructions)
    /// Useful for instructions that need sign extension but store result in unsigned register.
    /// </summary>
    /// <param name="value">The value to sign-extend</param>
    /// <param name="fromSize">Source size in bits (8, 16, 32)</param>
    /// <param name="toSize">Destination size in bits (16, 32)</param>
    /// <returns>Sign-extended value as unsigned type</returns>
    public ValueNode SignExtendToUnsigned(ValueNode value, int fromSize, int toSize) {
        // Convert to signed source type, then to signed dest type, then to unsigned dest type
        ValueNode signExtended = SignExtend(value, fromSize, toSize);
        return TypeConversion.Convert(UType(toSize), signExtended);
    }

    /// <summary>
    /// Creates an ALU method call value node with variable number of operands.
    /// Examples:
    ///   - "Alu8.Inc(value)" - 1 operand
    ///   - "Alu8.Add(v1, v2)" - 2 operands
    ///   - "Alu16.Shld(rm, r, count)" - 3 operands
    /// </summary>
    /// <param name="resultType">The data type of the result</param>
    /// <param name="size">The ALU size (8, 16, or 32)</param>
    /// <param name="operation">The operation name (e.g., "Add", "Sub", "Inc", "Shld")</param>
    /// <param name="operands">Variable number of operands</param>
    /// <returns>MethodCallValueNode representing the ALU operation</returns>
    public MethodCallValueNode AluCall(DataType resultType, int size, string operation, params ValueNode[] operands) {
        return new MethodCallValueNode(resultType, $"Alu{size}", operation, operands);
    }

    /// <summary>
    /// Creates an ALU method call and declares a variable to hold the result.
    /// Example: "uint result = Alu8.Mul(v1, v2);"
    /// </summary>
    /// <param name="resultType">The data type of the result</param>
    /// <param name="size">The ALU size (8, 16, or 32)</param>
    /// <param name="operation">The operation name (e.g., "Mul", "Div")</param>
    /// <param name="variableName">Optional variable name (defaults to "result")</param>
    /// <param name="operands">Variable number of operands</param>
    /// <returns>VariableDeclarationNode with the ALU call as initializer</returns>
    public VariableDeclarationNode DeclareAluResult(DataType resultType, int size, string operation, string variableName, params ValueNode[] operands) {
        MethodCallValueNode aluCall = AluCall(resultType, size, operation, operands);
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
    /// <param name="highRegName">Name of the high register (e.g., "DX", "EDX")</param>
    /// <param name="lowRegName">Name of the low register (e.g., "AX", "EAX")</param>
    /// <param name="size">Size of individual registers in bits (16 or 32)</param>
    /// <param name="resultType">The result type (signed wide type)</param>
    /// <returns>Combined value as signed wide type</returns>
    public ValueNode CombineHighLowRegisters(string highRegName, string lowRegName, int size, DataType resultType) {
        DataType wideUnsignedType = UType(size * 2);

        ValueNode highReg = Register.RegByName(highRegName);
        ValueNode lowReg = Register.RegByName(lowRegName);

        // Convert to wide unsigned for bit operations
        ValueNode highWide = TypeConversion.Convert(wideUnsignedType, highReg);
        ValueNode lowWide = TypeConversion.Convert(wideUnsignedType, lowReg);

        // Shift high left by size bits
        BinaryOperationNode shiftedHigh = new BinaryOperationNode(wideUnsignedType, highWide, BinaryOperation.LEFT_SHIFT, Constant.ToNode(size));

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
    /// <param name="shiftAmount">Number of bits to shift right</param>
    /// <param name="targetType">Target type for the extracted value</param>
    /// <returns>Upper bits converted to target type</returns>
    public ValueNode ExtractUpperBits(VariableReferenceNode source, int shiftAmount, DataType targetType) {
        BinaryOperationNode shiftRight = new BinaryOperationNode(source.DataType, source, BinaryOperation.RIGHT_SHIFT, Constant.ToNode(shiftAmount));
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