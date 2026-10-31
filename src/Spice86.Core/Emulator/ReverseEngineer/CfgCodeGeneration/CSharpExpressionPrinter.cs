namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Linq;
using System.Reflection;

/// <summary>
/// Prints an AST expression node as a <see cref="CSharpFragment"/>. Reads no CFG context; the only state is
/// the local variable suffix of the instruction being printed.
/// </summary>
internal sealed class CSharpExpressionPrinter : ICodeGenerationAstVisitor<CSharpFragment> {
    internal const int AssignmentPrecedence = 1;
    private const int ConditionalPrecedence = 2;
    private const int LogicalOrPrecedence = 3;
    private const int LogicalAndPrecedence = 4;
    private const int BitwiseOrPrecedence = 5;
    private const int BitwiseXorPrecedence = 6;
    private const int BitwiseAndPrecedence = 7;
    private const int EqualityPrecedence = 8;
    private const int RelationalPrecedence = 9;
    private const int ShiftPrecedence = 10;
    private const int AdditivePrecedence = 11;
    private const int MultiplicativePrecedence = 12;

    /// <summary>Binding strength of a prefix operator such as <c>!</c> or <c>-</c>.</summary>
    internal const int UnaryPrecedence = 13;
    private const int CastPrecedence = 13;

    private static readonly Dictionary<Type, ClrTypeInfo> ClrTypesByType = new() {
        [typeof(byte)] = new ClrTypeInfo("byte", DataType.UINT8),
        [typeof(sbyte)] = new ClrTypeInfo("sbyte", DataType.INT8),
        [typeof(ushort)] = new ClrTypeInfo("ushort", DataType.UINT16),
        [typeof(short)] = new ClrTypeInfo("short", DataType.INT16),
        [typeof(uint)] = new ClrTypeInfo("uint", DataType.UINT32),
        [typeof(int)] = new ClrTypeInfo("int", DataType.INT32),
        [typeof(ulong)] = new ClrTypeInfo("ulong", DataType.UINT64),
        [typeof(long)] = new ClrTypeInfo("long", DataType.INT64),
        [typeof(bool)] = new ClrTypeInfo("bool", null)
    };

    private readonly RegisterRenderer _registerRenderer = new(AsmRenderingConfig.CreateSpice86Style());
    private readonly IReadOnlyDictionary<ushort, string> _segmentVariables;

    /// <summary>
    /// Constructs a printer wired with the segment-variable names generated code refers to.
    /// </summary>
    public CSharpExpressionPrinter(IReadOnlyDictionary<ushort, string> segmentVariables) {
        _segmentVariables = segmentVariables;
    }

    /// <summary>The suffix appended to local variable names of the instruction being printed; empty for none.</summary>
    public string LocalVariableSuffix { get; set; } = string.Empty;

    /// <summary>Prints <paramref name="node"/>, which must be an expression.</summary>
    public CSharpFragment Print(IVisitableAstNode node) => node.Accept(this);

    /// <summary>The name of the local <paramref name="variableName"/> with the current suffix.</summary>
    public string LocalVariableName(string variableName) =>
        LocalVariableSuffix.Length == 0 ? variableName : $"{variableName}_{LocalVariableSuffix}";

    /// <summary>The generated variable name holding <paramref name="segment"/>.</summary>
    /// <exception cref="InvalidOperationException">No variable was generated for the segment, which is a generator bug.</exception>
    public string SegmentVariable(ushort segment) {
        if (!_segmentVariables.TryGetValue(segment, out string? name)) {
            throw new InvalidOperationException($"No segment variable was generated for segment 0x{segment:X4}.");
        }
        return name;
    }

    private CSharpFragment Expr(IVisitableAstNode node) => node.Accept(this);

    // ----------------------------------------------------------------------------------------------------
    // Expression visit methods. These need no CFG context: they lower a node directly from its own syntax.
    // ----------------------------------------------------------------------------------------------------

    public CSharpFragment VisitMethodCallNode(MethodCallNode node) {
        if (node is PortWriteCallNode port) {
            return PortWrite(port);
        }
        string target = node.PropertyPath is null ? string.Empty : node.PropertyPath + ".";
        string arguments = string.Join(", ", FormatMethodArguments(node));
        return $"{target}{node.MethodName}({arguments})";
    }

    private static readonly
        Dictionary<(Type Type, string Name, int ArgumentCount), ParameterInfo[]?> ParametersByCall = [];

    private IEnumerable<string> FormatMethodArguments(MethodCallNode node) {
        Type? targetType = ResolveKnownMethodTargetType(node.PropertyPath);
        if (targetType is null) {
            return node.Arguments.Select(argument => Expr(argument).Text);
        }

        ParameterInfo[]? parameters;
        lock (ParametersByCall) {
            if (!ParametersByCall.TryGetValue((targetType, node.MethodName, node.Arguments.Count),
                    out parameters)) {
                List<MethodInfo> candidates = targetType.GetMethods()
                    .Where(method => method.Name == node.MethodName
                        && method.GetParameters().Length == node.Arguments.Count)
                    .ToList();
                if (candidates.Count > 1) {
                    throw new NotSupportedException(
                        $"{targetType.Name}.{node.MethodName} has {candidates.Count} overloads taking {node.Arguments.Count} arguments; the generator cannot pick parameter types for argument casts.");
                }
                parameters = candidates.Count == 1 ? candidates[0].GetParameters() : null;
                ParametersByCall[(targetType, node.MethodName, node.Arguments.Count)] = parameters;
            }
        }
        if (parameters is null) {
            return node.Arguments.Select(argument => Expr(argument).Text);
        }

        return node.Arguments.Select((argument, index) => Argument(parameters[index].ParameterType, argument));
    }

    /// <summary>
    /// An argument for a parameter of <paramref name="parameterType"/>. A constant is written as a literal of the
    /// parameter type, without a cast: C# converts a constant argument implicitly when its value fits. An
    /// argument already converted to the signed type of the parameter's width is passed through: casting it
    /// back to the parameter type is the identity.
    /// </summary>
    private string Argument(Type parameterType, IVisitableAstNode argument) {
        DataType? parameterDataType = TryToDataType(parameterType);
        if (parameterDataType is not null
            && SameWidthSignRoundTripOperand(parameterDataType, argument) is ValueNode unwrapped) {
            return Expr(unwrapped).Text;
        }
        CSharpFragment fragment = Expr(argument);
        if (fragment.Constant is ConstantNode constant && parameterDataType is not null && parameterDataType != DataType.BOOL) {
            return ConstantLiteral(new ConstantNode(parameterDataType, constant.ConvertAsCSharpCast(parameterDataType))).Text;
        }
        return Cast(parameterType, fragment);
    }

    private static Type? ResolveKnownMethodTargetType(string? propertyPath) => propertyPath switch {
        nameof(Alu8) => typeof(Alu8),
        nameof(Alu16) => typeof(Alu16),
        nameof(Alu32) => typeof(Alu32),
        nameof(Stack) => typeof(Stack),
        _ => null
    };

    public CSharpFragment VisitMethodCallValueNode(MethodCallValueNode node) {
        if (node is PortReadCallNode port) {
            return PortRead(port);
        }
        return Atomic(Expr(node.CallNode), node.DataType);
    }

    /// <summary>Prints an <c>InN((ushort)port)</c> read.</summary>
    private CSharpFragment PortRead(PortReadCallNode node) {
        string port = Cast(DataType.UINT16, Expr(node.CallNode.Arguments[0]));
        return Atomic($"In{(int)node.Width}({port})", node.DataType);
    }

    public CSharpFragment VisitRegisterNode(RegisterNode node) =>
        Atomic(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex), node.DataType);

    public CSharpFragment VisitSegmentRegisterNode(SegmentRegisterNode node) =>
        Atomic(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex), DataType.UINT16);

    public CSharpFragment VisitSegmentedPointer(SegmentedPointerNode node) {
        string indexer = ToMemoryIndexer(node.DataType);
        string segment = Expr(node.Segment);
        CSharpFragment offset = Expr(node.Offset);
        return Atomic($"{indexer}[{segment}, {MemoryOffset(offset)}]", node.DataType);
    }

    public CSharpFragment VisitAbsolutePointerNode(AbsolutePointerNode node) =>
        Atomic($"{ToMemoryIndexer(node.DataType)}[{Cast(DataType.UINT32, Expr(node.AbsoluteAddress))}]", node.DataType);

    public CSharpFragment VisitConditionalValueNode(ConditionalValueNode node) {
        if (node.Condition is ConditionalValueNode) {
            throw Unsupported(node);
        }
        string text = $"{Parenthesize(Expr(node.Condition), ConditionalPrecedence + 1)} ? "
            + $"{Expr(node.WhenTrue)} : {Expr(node.WhenFalse)}";
        return new CSharpFragment(text, node.DataType, ConditionalPrecedence);
    }

    public CSharpFragment VisitConstantNode(ConstantNode node) => ConstantLiteral(node);

    public CSharpFragment VisitNearAddressNode(NearAddressNode node) => VisitConstantNode(node);

    public CSharpFragment VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation == BinaryOperation.ASSIGN) {
            // No outer parentheses: as a statement the consumer appends `;` (`x = y;`); as a sub-expression
            // the low assignment precedence makes Parenthesize wrap it (`(x = y)`).
            string assignment = $"{Expr(node.Left)} = {EmitAssignmentRight(node)}";
            return new CSharpFragment(assignment, node.DataType, AssignmentPrecedence);
        }

        CSharpFragment leftFragment = Expr(node.Left);
        CSharpFragment rightFragment = Expr(node.Right);
        if (TrySimplifyBinaryOperation(node.BinaryOperation, node.DataType.BitWidth, leftFragment, rightFragment) is CSharpFragment simplified) {
            return simplified;
        }

        (string op, int precedence) = node.BinaryOperation switch {
            BinaryOperation.MULTIPLY => ("*", MultiplicativePrecedence),
            BinaryOperation.DIVIDE => ("/", MultiplicativePrecedence),
            BinaryOperation.MODULO => ("%", MultiplicativePrecedence),
            BinaryOperation.PLUS => ("+", AdditivePrecedence),
            BinaryOperation.MINUS => ("-", AdditivePrecedence),
            BinaryOperation.LEFT_SHIFT => ("<<", ShiftPrecedence),
            BinaryOperation.RIGHT_SHIFT => (">>", ShiftPrecedence),
            BinaryOperation.LESS_THAN => ("<", RelationalPrecedence),
            BinaryOperation.GREATER_THAN => (">", RelationalPrecedence),
            BinaryOperation.LESS_THAN_OR_EQUAL => ("<=", RelationalPrecedence),
            BinaryOperation.GREATER_THAN_OR_EQUAL => (">=", RelationalPrecedence),
            BinaryOperation.EQUAL => ("==", EqualityPrecedence),
            BinaryOperation.NOT_EQUAL => ("!=", EqualityPrecedence),
            BinaryOperation.BITWISE_AND => ("&", BitwiseAndPrecedence),
            BinaryOperation.BITWISE_XOR => ("^", BitwiseXorPrecedence),
            BinaryOperation.BITWISE_OR => ("|", BitwiseOrPrecedence),
            BinaryOperation.LOGICAL_AND => ("&&", LogicalAndPrecedence),
            BinaryOperation.LOGICAL_OR => ("||", LogicalOrPrecedence),
            _ => throw Unsupported(node)
        };

        // Left-associative: the left operand may share the operator's precedence without parentheses; the
        // right operand needs them when it binds equally or more loosely.
        string left = Parenthesize(leftFragment, precedence);
        string right = Parenthesize(rightFragment, precedence + 1);
        if (node.BinaryOperation is BinaryOperation.RIGHT_SHIFT or BinaryOperation.DIVIDE
                or BinaryOperation.MODULO or BinaryOperation.LESS_THAN or BinaryOperation.GREATER_THAN
                or BinaryOperation.LESS_THAN_OR_EQUAL or BinaryOperation.GREATER_THAN_OR_EQUAL
                or BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL) {
            // These results depend on the bits above the operand width, which C# computes in int:
            // a promoted operand is truncated back to its own type first.
            left = TruncatedOperand(node.Left, leftFragment) ?? left;
            right = TruncatedOperand(node.Right, rightFragment) ?? right;
        }
        if (node.BinaryOperation is BinaryOperation.LEFT_SHIFT or BinaryOperation.RIGHT_SHIFT
            && rightFragment.Constant is ConstantNode shiftCount) {
            right = ShiftCountText(shiftCount);
        }
        // C# promotes byte/ushort/short/sbyte arithmetic to int, so the actual evaluated type is not the
        // node's semantic DataType; leave it unknown so a cast back to the operand width is never elided.
        DataType? resultType = node.DataType == DataType.BOOL ? DataType.BOOL : null;
        return new CSharpFragment($"{left} {op} {right}", resultType, precedence);
    }

    /// <summary>The operand text with a cast back to its own width, when the fragment lost its type to
    /// C# int promotion, the operand is an 8- or 16-bit integer and its value can actually be outside
    /// that width; otherwise <c>null</c>.</summary>
    private static string? TruncatedOperand(ValueNode operand, CSharpFragment fragment) {
        if (fragment.Type is null && operand.DataType.BitWidth is BitWidth.BYTE_8 or BitWidth.WORD_16
            && MayExceedWidth(operand)) {
            return Cast(operand.DataType, fragment);
        }
        return null;
    }

    /// <summary>Whether the int value a promoted expression computes can be outside its own
    /// <see cref="ValueNode.DataType"/>: registers, memory reads, variables and constants that fit are
    /// bounded; additive, multiplicative and shift-left growth and the sign of <c>~x</c>/<c>-x</c> are
    /// not; a bitwise <c>&amp;</c> is bounded by either in-range side, <c>|</c>/<c>^</c> only when both
    /// sides are; <c>&gt;&gt;</c>, <c>/</c> and <c>%</c> keep the left side's range.</summary>
    private static bool MayExceedWidth(ValueNode node) => node switch {
        UnaryOperationNode => true,
        BinaryOperationNode binary => binary.BinaryOperation switch {
            BinaryOperation.PLUS or BinaryOperation.MINUS or BinaryOperation.MULTIPLY
                or BinaryOperation.LEFT_SHIFT => true,
            BinaryOperation.BITWISE_OR or BinaryOperation.BITWISE_XOR =>
                MayExceedWidth(binary.Left) || MayExceedWidth(binary.Right),
            BinaryOperation.BITWISE_AND =>
                MayExceedWidth(binary.Left) && MayExceedWidth(binary.Right),
            BinaryOperation.RIGHT_SHIFT or BinaryOperation.DIVIDE or BinaryOperation.MODULO =>
                MayExceedWidth(binary.Left),
            _ => true
        },
        InstructionFieldNode field => MayExceedWidth(field.ResolvedNode),
        ConstantNode constant => constant.Value > MaxOf(constant.DataType.BitWidth),
        RegisterNode or SegmentRegisterNode or VariableReferenceNode
            or SegmentedPointerNode or AbsolutePointerNode => false,
        TypeConversionNode or ExplicitCastNode => false,
        _ => true
    };

    private static ulong MaxOf(BitWidth width) => width switch {
        BitWidth.BYTE_8 => byte.MaxValue,
        BitWidth.WORD_16 => ushort.MaxValue,
        BitWidth.DWORD_32 => uint.MaxValue,
        _ => ulong.MaxValue
    };

    private static CSharpFragment? TrySimplifyBinaryOperation(BinaryOperation operation, BitWidth operationWidth, CSharpFragment left, CSharpFragment right) {
        if (operation is BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL) {
            return TrySimplifyBoolComparison(operation == BinaryOperation.EQUAL, left, right);
        }
        if (operation == BinaryOperation.PLUS && right.Constant is ConstantNode constant) {
            return TrySimplifyConstantAddition(left, constant, operationWidth);
        }
        if (operation == BinaryOperation.MINUS && left.Constant is ConstantNode zero
            && zero.DataType != DataType.BOOL && zero.Value == 0) {
            return new CSharpFragment($"-{Parenthesize(right, UnaryPrecedence + 1)}", null, UnaryPrecedence);
        }
        return null;
    }

    private static CSharpFragment? TrySimplifyBoolComparison(bool isEqual, CSharpFragment left, CSharpFragment right) {
        if (right.Constant is ConstantNode rightConstant && rightConstant.DataType == DataType.BOOL
            && left.Constant is null && left.Type == DataType.BOOL) {
            return BoolComparisonResult(isEqual, left, rightConstant);
        }
        if (left.Constant is ConstantNode leftConstant && leftConstant.DataType == DataType.BOOL
            && right.Constant is null && right.Type == DataType.BOOL) {
            return BoolComparisonResult(isEqual, right, leftConstant);
        }
        return null;
    }

    private static CSharpFragment BoolComparisonResult(bool isEqual, CSharpFragment operand, ConstantNode boolConstant) {
        bool keepOperand = (boolConstant.Value != 0) == isEqual;
        if (keepOperand) {
            return operand;
        }
        return new CSharpFragment($"!{Parenthesize(operand, UnaryPrecedence)}", DataType.BOOL, UnaryPrecedence);
    }

    private static CSharpFragment? TrySimplifyConstantAddition(CSharpFragment left, ConstantNode constant, BitWidth operationWidth) {
        if (constant.Value == 0) {
            return left;
        }
        if (!ConstantEvaluation.TrySubtractionMagnitude(constant, operationWidth, out ulong magnitudeValue)) {
            return null;
        }
        ConstantNode magnitude = new(DataType.UnsignedFromBitWidth(constant.DataType.BitWidth),
            magnitudeValue);
        return new CSharpFragment($"{Parenthesize(left, AdditivePrecedence)} - {ConstantLiteral(magnitude).Text}", null, AdditivePrecedence);
    }

    public CSharpFragment VisitUnaryOperationNode(UnaryOperationNode node) {
        string op = node.UnaryOperation switch {
            UnaryOperation.NOT => "!",
            UnaryOperation.NEGATE => "-",
            UnaryOperation.BITWISE_NOT => "~",
            _ => throw Unsupported(node)
        };
        int operandPrecedence = node.UnaryOperation == UnaryOperation.NEGATE ? UnaryPrecedence + 1 : UnaryPrecedence;
        string operand = Parenthesize(Expr(node.Value), operandPrecedence);
        // Logical NOT yields bool; arithmetic/bitwise unary ops promote to int, so their evaluated type is
        // not the node's semantic DataType.
        DataType? resultType = node.UnaryOperation == UnaryOperation.NOT ? DataType.BOOL : null;
        return new CSharpFragment($"{op}{operand}", resultType, UnaryPrecedence);
    }

    public CSharpFragment VisitTypeConversionNode(TypeConversionNode node) {
        CSharpFragment inner = Expr(node.Value);
        if (inner.Constant is ConstantNode constant) {
            return ConstantLiteral(new ConstantNode(node.DataType, constant.ConvertAsCSharpCast(node.DataType)));
        }
        if (inner.Type == node.DataType || node.DataType == DataType.BOOL && inner.Type == DataType.BOOL) {
            // Converting to a type the value already has is a no-op; keep the inner fragment as-is.
            return inner;
        }
        if (SameWidthSignRoundTripOperand(node.DataType, node.Value) is ValueNode unwrapped) {
            // Conversion to the signed type of the same width and back is the identity.
            return Expr(unwrapped);
        }
        if (inner.Type == DataType.BOOL && node.DataType != DataType.BOOL) {
            return new CSharpFragment($"({inner.Text} ? 1 : 0)", null, CSharpFragment.AtomicPrecedence);
        }
        string text = Cast(node.DataType, inner);
        return new CSharpFragment(text, node.DataType, CastPrecedence);
    }

    public CSharpFragment VisitFlagRegisterNode(FlagRegisterNode node) =>
        node.DataType.BitWidth == BitWidth.WORD_16
            ? Atomic("FlagRegister16", DataType.UINT16)
            : Atomic("FlagRegister", DataType.UINT32);

    public CSharpFragment VisitCpuFlagNode(CpuFlagNode node) => Atomic(node.FlagMask switch {
        Flags.Carry => "CarryFlag",
        Flags.Parity => "ParityFlag",
        Flags.Auxiliary => "AuxiliaryFlag",
        Flags.Zero => "ZeroFlag",
        Flags.Sign => "SignFlag",
        Flags.Trap => "TrapFlag",
        Flags.Interrupt => "InterruptFlag",
        Flags.Direction => "DirectionFlag",
        Flags.Overflow => "OverflowFlag",
        _ => throw Unsupported(node)
    }, DataType.BOOL);

    public CSharpFragment VisitInstructionFieldNode(InstructionFieldNode node) => Expr(node.ResolvedNode);
    public CSharpFragment VisitVariableReferenceNode(VariableReferenceNode node) =>
        Atomic(node is MethodScopedVariableReferenceNode ? node.VariableName : LocalVariableName(node.VariableName),
            node.DataType);
    public CSharpFragment VisitVariableDeclarationNode(VariableDeclarationNode node) =>
        $"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)} = {Cast(node.DataType, Expr(node.Initializer))}";
    public CSharpFragment VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        $"new SegmentedAddress({Cast(DataType.UINT16, Expr(node.Segment))}, {Cast(DataType.UINT16, Expr(node.Offset))})";

    private string EmitAssignmentRight(BinaryOperationNode assignment) {
        CSharpFragment expression = Expr(assignment.Right);
        return Cast(assignment.Left.DataType, expression);
    }

    private static string ToMemoryIndexer(DataType dataType) => dataType.BitWidth switch {
        BitWidth.BYTE_8 => dataType.Signed ? "Int8" : "UInt8",
        BitWidth.WORD_16 => dataType.Signed ? "Int16" : "UInt16",
        BitWidth.DWORD_32 or BitWidth.BOOL_1 => dataType.Signed ? "Int32" : "UInt32",
        _ => throw new NotSupportedException($"Unsupported memory data type {dataType}.")
    };

    private static string ToCSharpType(DataType dataType) {
        if (dataType == DataType.BOOL) {
            return "bool";
        }

        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? "sbyte" : "byte",
            BitWidth.WORD_16 => dataType.Signed ? "short" : "ushort",
            BitWidth.DWORD_32 or BitWidth.BOOL_1 => dataType.Signed ? "int" : "uint",
            BitWidth.QWORD_64 => dataType.Signed ? "long" : "ulong",
            _ => throw new NotSupportedException($"Unsupported data type {dataType}.")
        };
    }

    internal static string Cast(DataType dataType, CSharpFragment expression) {
        if (dataType == DataType.BOOL) {
            // A bool expression never needs a cast; a non-bool would not be assignable to bool anyway.
            return expression.Text;
        }
        if (expression.Type == dataType) {
            // Already the target type: no cast needed.
            return expression.Text;
        }

        string targetType = ToCSharpType(dataType);
        string inner = Parenthesize(expression, CastPrecedence);
        return $"({targetType}){inner}";
    }

    private static string Cast(Type targetType, CSharpFragment expression) {
        if (targetType == typeof(bool)) {
            return expression.Text;
        }

        DataType? csharpType = TryToDataType(targetType);
        if (csharpType is not null) {
            return Cast(csharpType, expression);
        }

        return expression.Text;
    }

    private static CSharpFragment Atomic(string text, DataType type) => new(text, type, CSharpFragment.AtomicPrecedence);

    /// <summary>A shift count is a count, not a value of the operand's type: it always prints in decimal.</summary>
    internal static string ShiftCountText(ConstantNode shiftCount) =>
        shiftCount.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a constant as a C# literal: magnitudes 0-9 in decimal, larger ones in hex zero-padded to the
    /// operand width, a leading minus for negative signed values, and a suffix where C# needs one.
    /// </summary>
    internal static CSharpFragment ConstantLiteral(ConstantNode node) {
        if (node.DataType == DataType.BOOL) {
            return new CSharpFragment(node.Value == 0 ? "false" : "true", DataType.BOOL, CSharpFragment.AtomicPrecedence) { Constant = node };
        }
        string? minValueName = MinValueName(node);
        if (minValueName is not null) {
            return new CSharpFragment(minValueName, node.DataType, CSharpFragment.AtomicPrecedence) { Constant = node };
        }
        bool negative = node.IsNegative;
        ulong magnitude = negative ? (ulong)(-node.SignedValue) : node.Value;
        string sign = negative ? "-" : "";
        string text = sign + FormatMagnitude(magnitude, node.DataType.BitWidth) + LiteralSuffix(node.DataType, node.Value);
        int precedence = negative ? UnaryPrecedence : CSharpFragment.AtomicPrecedence;
        return new CSharpFragment(text, node.DataType, precedence) { Constant = node };
    }

    private static string? MinValueName(ConstantNode node) {
        if (node.DataType == DataType.INT32 && node.SignedValue == int.MinValue) {
            return "int.MinValue";
        }
        if (node.DataType == DataType.INT64 && node.SignedValue == long.MinValue) {
            return "long.MinValue";
        }
        return null;
    }

    private static string FormatMagnitude(ulong value, BitWidth width) {
        if (value <= 9) {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        int hexDigits = width switch {
            BitWidth.BYTE_8 => 2,
            BitWidth.WORD_16 => 4,
            BitWidth.DWORD_32 or BitWidth.BOOL_1 => 8,
            BitWidth.QWORD_64 => 16,
            _ => throw new NotSupportedException($"Unsupported bit width {width}")
        };
        return "0x" + value.ToString("X" + hexDigits, CultureInfo.InvariantCulture);
    }

    private static string LiteralSuffix(DataType dataType, ulong value) {
        if (dataType.BitWidth == BitWidth.QWORD_64) {
            return dataType.Signed ? "L" : "UL";
        }
        if (!dataType.Signed && dataType.BitWidth == BitWidth.DWORD_32 && value > int.MaxValue) {
            return "u";
        }
        return "";
    }

    /// <summary>
    /// Renders a memory-indexer offset. A constant or single register/variable offset is emitted directly
    /// (the indexer has a <c>ushort</c> overload); a compound arithmetic offset is wrapped in
    /// <c>(ushort)(...)</c> so wrap-around matches real-mode 16-bit offset arithmetic.
    /// </summary>
    private static string MemoryOffset(CSharpFragment offset) {
        if (offset.Precedence >= CSharpFragment.AtomicPrecedence) {
            return offset.Text;
        }
        return $"(ushort)({offset.Text})";
    }

    /// <summary>
    /// Wraps <paramref name="fragment"/> in parentheses only when its top-level operator binds more loosely
    /// than <paramref name="minimumPrecedence"/> requires.
    /// </summary>
    internal static string Parenthesize(CSharpFragment fragment, int minimumPrecedence) {
        if (fragment.Precedence >= minimumPrecedence) {
            return fragment.Text;
        }
        return $"({fragment.Text})";
    }

    private static string ToCSharpType(Type type) =>
        ClrTypesByType.TryGetValue(type, out ClrTypeInfo info) ? info.Name : type.FullName ?? type.Name;

    private static DataType? TryToDataType(Type type) =>
        ClrTypesByType.TryGetValue(type, out ClrTypeInfo info) ? info.DataType : null;

    internal static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static NotSupportedException Unsupported(IVisitableAstNode node) =>
        new($"CFG C# generation does not support AST node {node.GetType().FullName} yet.");

    // ----------------------------------------------------------------------------------------------------
    // Code-generation value nodes. These are expressions printed from pre-resolved identifiers.
    // ----------------------------------------------------------------------------------------------------

    public CSharpFragment VisitSegmentVariableNode(SegmentVariableNode node) => Atomic(SegmentVariable(node.Segment), node.DataType);
    public CSharpFragment VisitOffsetLiteralNode(OffsetLiteralNode node) => Atomic($"0x{node.Value:X4}", node.DataType);
    public CSharpFragment VisitInstructionPointerNode(InstructionPointerNode node) => Atomic("IP", node.DataType);
    public CSharpFragment VisitLoadOffsetParameterNode(LoadOffsetParameterNode node) => Atomic("loadOffset", node.DataType);

    public CSharpFragment VisitSegmentedAddressListNode(SegmentedAddressListNode node) =>
        $"[{string.Join(", ", node.Addresses.Select(address => $"new({SegmentVariable(address.Segment)}, 0x{address.Offset:X4})"))}]";

    public CSharpFragment VisitMethodGroupNode(MethodGroupNode node) {
        if (node.LoadOffset == 0) {
            return new CSharpFragment(node.MethodName);
        }
        return new CSharpFragment($"_ => {node.MethodName}(0x{node.LoadOffset:X4})", null, AssignmentPrecedence);
    }

    public CSharpFragment VisitSignatureFieldNode(SignatureFieldNode node) => new(node.FieldName);

    private static ValueNode? SameWidthSignRoundTripOperand(DataType target, IVisitableAstNode value) =>
        value switch {
            TypeConversionNode { DataType: var middle, Value: var inner }
                when middle != DataType.BOOL && target != DataType.BOOL
                     && middle.BitWidth == target.BitWidth && inner.DataType == target => inner,
            ExplicitCastNode { DataType: var middle, Value: ValueNode inner }
                when middle != DataType.BOOL && target != DataType.BOOL
                     && middle.BitWidth == target.BitWidth && inner.DataType == target => inner,
            _ => null
        };

    public CSharpFragment VisitExplicitCastNode(ExplicitCastNode node) {
        if (SameWidthSignRoundTripOperand(node.DataType, node.Value) is ValueNode unwrapped) {
            return Expr(unwrapped);
        }
        CSharpFragment inner = Expr(node.Value);
        string text = Cast(node.DataType, inner);
        if (text == inner.Text) {
            return inner;
        }
        return new CSharpFragment(text, node.DataType, CastPrecedence);
    }

    // ----------------------------------------------------------------------------------------------------
    // Statement and control-flow nodes are not expressions.
    // ----------------------------------------------------------------------------------------------------

    private static NotSupportedException NotAnExpression(IVisitableAstNode node) =>
        new($"{node.GetType().Name} is not an expression and cannot be printed by {nameof(CSharpExpressionPrinter)}.");

    public CSharpFragment VisitTupleSwapNode(TupleSwapNode node) => throw NotAnExpression(node);

    public CSharpFragment VisitBlockNode(BlockNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitIfElseNode(IfElseNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitWhileNode(WhileNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitWhileWithExitNode(WhileWithExitNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitThrowNode(ThrowNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitInstructionNode(InstructionNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitMoveIpNextNode(MoveIpNextNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCallNearNode(CallNearNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCallFarNode(CallFarNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitReturnNearNode(ReturnNearNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitReturnFarNode(ReturnFarNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitJumpNearNode(JumpNearNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitJumpFarNode(JumpFarNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitHltNode(HltNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitInterruptCallNode(InterruptCallNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitReturnInterruptNode(ReturnInterruptNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCallbackNode(CallbackNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitSelectorNode(SelectorNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitInvalidInstructionNode(InvalidInstructionNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCpuidNode(CpuidNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitGotoNode(GotoNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitLabelNode(LabelNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitEntryDispatcherLabelNode(EntryDispatcherLabelNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCyclicJumpNode(CyclicJumpNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitReturnNode(ReturnNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitSwitchNode(SwitchNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitBreakNode(BreakNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitContinueNode(ContinueNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitInfiniteLoopNode(InfiniteLoopNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitDoWhileNode(DoWhileNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCpuFaultTryCatchNode(CpuFaultTryCatchNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitFailAsUntestedNode(FailAsUntestedNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCommentNode(CommentNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitCheckExternalEventsNode(CheckExternalEventsNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitSpeculativeGuardNode(SpeculativeGuardNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitInstructionBoundaryNode(InstructionBoundaryNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitDivisionNode(DivisionNode node) => throw NotAnExpression(node);

    public CSharpFragment VisitAluOperationNode(AluOperationNode node) => node.Call.Accept(this);
    public CSharpFragment VisitAluStatementNode(AluStatementNode node) => node.Call.Accept(this);

    /// <summary>Prints an <c>OutN((ushort)port, value)</c> write.</summary>
    private CSharpFragment PortWrite(PortWriteCallNode node) {
        string port = Cast(DataType.UINT16, Expr(node.Arguments[0]));
        string value = Cast(PortValueType(node.Width), Expr(node.Arguments[1]));
        return new CSharpFragment($"Out{(int)node.Width}({port}, {value})");
    }

    private static DataType PortValueType(BitWidth width) => width switch {
        BitWidth.BYTE_8 => DataType.UINT8,
        BitWidth.WORD_16 => DataType.UINT16,
        _ => DataType.UINT32
    };
    public CSharpFragment VisitOpaqueNode(OpaqueNode node) => node.Original.Accept(this);
    public CSharpFragment VisitInstructionStartNode(InstructionStartNode node) => $"// {node.Comment}";
    public CSharpFragment VisitInterruptReturnNode(InterruptReturnNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitSetInterruptShadowNode(SetInterruptShadowNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitPreTestLoopNode(PreTestLoopNode node) => throw NotAnExpression(node);
    public CSharpFragment VisitLocalDeclarationNode(LocalDeclarationNode node) =>
        $"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)}";

    private readonly record struct ClrTypeInfo(string Name, DataType? DataType);
}
