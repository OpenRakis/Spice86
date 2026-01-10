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
    }

    public RegisterAstBuilder Register { get; } = new();
    public PointerAstBuilder Pointer { get; } = new();
    public ConstantAstBuilder Constant { get; } = new();
    public FlagAstBuilder Flag { get; } = new();
    public TypeConversionAstBuilder TypeConversion { get; } = new();
    public InstructionFieldAstBuilder InstructionField { get; }
    public ModRmAstBuilder ModRm { get; }

    public DataType SType(int size) {
        return Type(size, true);
    }
    public DataType UType(int size) {
        return Type(size, false);
    }

    private DataType Type(int size, bool isSigned) {
        return size switch {
            8 => isSigned ? DataType.INT8 : DataType.UINT8,
            16 => isSigned ? DataType.INT16 : DataType.UINT16,
            32 => isSigned ? DataType.INT32 : DataType.UINT32,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "value not handled")
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
    /// Conditionally wraps a value in an assignment if assign is true, otherwise returns the value.
    /// Useful for instructions that optionally assign results (e.g., CMP vs ADD).
    /// </summary>
    /// <param name="dataType">The data type</param>
    /// <param name="destination">The destination node (only used if assign is true)</param>
    /// <param name="source">The source value</param>
    /// <param name="assign">Whether to create an assignment</param>
    /// <returns>Assignment node if assign is true, otherwise the source node</returns>
    public IVisitableAstNode ConditionalAssign(DataType dataType, ValueNode destination, IVisitableAstNode source, bool assign) {
        return assign ? Assign(dataType, destination, (ValueNode)source) : source;
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

}