namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

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
    private readonly AnalyzedMethod? _method;
    private readonly ProgramAnalysis? _analysis;

    /// <summary>
    /// Constructs a printer wired with the segment-variable names generated code refers to, the
    /// method being printed and the program-wide values of the generation run.
    /// </summary>
    /// <param name="segmentVariables">The names generated code refers to, keyed by segment.</param>
    /// <param name="ioPorts">The ports the machine the program was dumped from had a registered
    /// device handler for, keyed by port to the handler's type; <see langword="null"/> suppresses
    /// the <c>no device</c> comment.</param>
    /// <param name="method">The analyzed method being printed.</param>
    /// <param name="analysis">The program-wide values of the generation run.</param>
    public CSharpExpressionPrinter(IReadOnlyDictionary<ushort, string> segmentVariables,
        IReadOnlyDictionary<ushort, Type>? ioPorts, AnalyzedMethod method, ProgramAnalysis analysis)
        : this(method, analysis, segmentVariables, ioPorts) {
    }

    /// <summary>Constructs a printer for a print that is not a generated method (dumps, tests).</summary>
    public CSharpExpressionPrinter(IReadOnlyDictionary<ushort, string> segmentVariables)
        : this(null, null, segmentVariables, null) {
    }

    private CSharpExpressionPrinter(AnalyzedMethod? method, ProgramAnalysis? analysis,
        IReadOnlyDictionary<ushort, string> segmentVariables, IReadOnlyDictionary<ushort, Type>? ioPorts) {
        _segmentVariables = segmentVariables;
        IoPorts = ioPorts;
        _method = method;
        _analysis = analysis;
    }

    /// <summary>The suffix appended to local variable names of the instruction being printed; empty for none.</summary>
    public string LocalVariableSuffix { get; set; } = string.Empty;

    /// <summary>The ports the machine the program was dumped from had a registered device handler
    /// for, keyed by port to the handler's type; <see langword="null"/> when the program was built
    /// without a machine, which suppresses the <c>no device</c> comment.</summary>
    internal IReadOnlyDictionary<ushort, Type>? IoPorts { get; }

    /// <summary>The class every global access prints through; unset, every global prints through
    /// the register class of its segment register.</summary>
    internal GlobalAccessClassification? GlobalClasses => _analysis?.GlobalClasses;

    /// <summary>The program's typed signatures, set before any method is printed: delegate
    /// references and partition returns of a typed method use its <c>_Asm</c> wrapper name, and a
    /// single-target call site to a typed entry prints the typed call form.</summary>
    internal Ir.TypedSignatures? TypedSignatures => _analysis?.TypedSignatures;

    /// <summary>The frame slot plan of the method being printed: pointer nodes of its table print
    /// as <c>frame.&lt;Name&gt;</c>.</summary>
    internal Ir.Memory.FrameSlotPlan? FramePlan => _method?.Prepared.FramePlan;

    /// <summary>The signature of the typed method being printed, <see langword="null"/> for
    /// untyped methods: its <c>ret</c> / <c>retf</c> returns print the return value.</summary>
    internal Ir.TypedSignature? TypedSignature => _method?.Signature;

    /// <summary>The read-position register and flag occurrences of the typed method being printed
    /// that substitute with their parameter name, keyed by node reference; <see langword="null"/>
    /// outside a typed method. Assignment targets are never in the table.</summary>
    internal IReadOnlyDictionary<IVisitableAstNode, string>? ParameterReads => _method?.ParameterReads;

    /// <summary>The register occurrences of the method being printed that substitute with a register
    /// local's printed text and fragment type, keyed by node reference; <see langword="null"/> when the
    /// method promotes no web. Definition targets are in the table: they print the bare name so the
    /// assignment's right side casts to the local's type.</summary>
    internal IReadOnlyDictionary<IVisitableAstNode, SubstitutedRead>? LocalReads =>
        _method?.RegisterLocals is { Reads.Count: > 0 } locals ? locals.Reads : null;

    /// <summary>The goto targets of the whole method being printed. A <c>for</c> rewrite refuses
    /// a loop when any statement of the method - not only of the block being dispatched - jumps to
    /// its header or its step.</summary>
    internal GotoTargets MethodGotoTargets => _method?.Prepared.Targets ?? GotoTargets.None;

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
        if (node is ProcedureCallHelperNode call
            && TypedSignatures?.TypedCallOf(call) is { } signature
            && call.Arguments[^1] is MethodGroupNode targetGroup) {
            return TypedCall(call, signature, targetGroup.MethodName);
        }
        string target = node.PropertyPath is null ? string.Empty : node.PropertyPath + ".";
        string arguments = string.Join(", ", FormatMethodArguments(node));
        if (node is InterruptServiceCallNode { StringLiteral: { } literal }) {
            arguments += ", " + StringLiteral(literal);
        }
        return $"{target}{MethodName(node)}({arguments})";
    }

    /// <summary>The method <paramref name="node"/> calls: the <c>_Asm</c> wrapper when the target
    /// method is typed (a partition return or an untyped call-site reference never reaches the
    /// typed method itself).</summary>
    private string MethodName(MethodCallNode node) =>
        TypedSignatures?.DelegateNameOf(node.MethodName) ?? node.MethodName;

    /// <summary>The typed call-site form of <paramref name="call"/>:
    /// <c>AX = NearCall(retCs, retIp, () => f(AX, BX))</c>, the bare call for a void method.</summary>
    private CSharpFragment TypedCall(ProcedureCallHelperNode call, Ir.TypedSignature signature,
        string methodName) {
        string prefix = string.Join(", ", call.Arguments.Take(call.Arguments.Count - 1)
            .Select(argument => Expr(argument).Text));
        string invocation = $"{call.MethodName}({prefix}, () => {methodName}({signature.ArgumentList}))";
        return signature.Outputs.Count switch {
            0 => invocation,
            1 => $"{signature.Outputs[0].Register} = {invocation}",
            _ => $"({string.Join(", ", signature.Outputs.Select(output => output.Register))}) = {invocation}"
        };
    }

    private static string StringLiteral(string text) {
        StringBuilder literal = new("\"");
        foreach (char c in text) {
            literal.Append(c switch {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\t' => "\\t",
                '\n' => "\\n",
                '\r' => "\\r",
                _ => c
            });
        }
        return literal.Append('"').ToString();
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

        return node.Arguments.Select((argument, index) => Argument(parameters[index], argument));
    }

    /// <summary>
    /// An argument for <paramref name="parameter"/>. A constant is written as a literal of the
    /// parameter type, without a cast: C# converts a constant argument implicitly when its value fits. An
    /// argument already converted to the signed type of the parameter's width is passed through: casting it
    /// back to the parameter type is the identity.
    /// </summary>
    private string Argument(ParameterInfo parameter, IVisitableAstNode argument) {
        Type parameterType = parameter.ParameterType;
        DataType? parameterDataType = TryToDataType(parameterType);
        if (parameterDataType is not null
            && SameWidthSignRoundTripOperand(parameterDataType, argument) is ValueNode unwrapped) {
            return Expr(unwrapped).Text;
        }
        CSharpFragment fragment = Expr(argument);
        if (fragment.Constant is ConstantNode constant && parameterDataType is not null && parameterDataType != DataType.BOOL) {
            return parameter.Name == "count"
                ? ArgumentConstant(constant, parameterDataType)
                : ConstantLiteral(new ConstantNode(parameterDataType,
                    constant.ConvertAsCSharpCast(parameterDataType))).Text;
        }
        return Cast(parameterType, fragment);
    }

    /// <summary>
    /// A shift/rotate count argument written as a literal of <paramref name="parameterDataType"/>, hex-padded only
    /// to the smallest even width that holds the value: an x86 count is an 8-bit operand, so it prints
    /// <c>0xFF</c> instead of the <c>0x000000FF</c> its 32-bit AST node would give.
    /// </summary>
    private static string ArgumentConstant(ConstantNode constant, DataType parameterDataType) {
        ConstantNode converted = new(parameterDataType, constant.ConvertAsCSharpCast(parameterDataType));
        if (MinValueName(converted) is { } minValueName) {
            return minValueName;
        }
        bool negative = converted.IsNegative;
        ulong magnitude = negative ? (ulong)(-converted.SignedValue) : converted.Value;
        BitWidth width = magnitude switch {
            <= byte.MaxValue => BitWidth.BYTE_8,
            <= ushort.MaxValue => BitWidth.WORD_16,
            <= uint.MaxValue => BitWidth.DWORD_32,
            _ => BitWidth.QWORD_64
        };
        return (negative ? "-" : "") + FormatMagnitude(magnitude, width)
            + LiteralSuffix(parameterDataType, converted.Value);
    }

    private static Type? ResolveKnownMethodTargetType(string? propertyPath) => propertyPath switch {
        nameof(Alu8) => typeof(Alu8),
        nameof(Alu16) => typeof(Alu16),
        nameof(Alu32) => typeof(Alu32),
        nameof(Stack) => typeof(Stack),
        "Cmos" => typeof(RealTimeClock),
        "Pit" => typeof(PitTimer),
        "Vga" => typeof(VgaIoPortHandler),
        "Opl" => typeof(Opl3Fm),
        _ => null
    };

    public CSharpFragment VisitMethodCallValueNode(MethodCallValueNode node) {
        if (node is PortReadCallNode port) {
            return PortRead(port);
        }
        return Atomic(Expr(node.CallNode), node.DataType);
    }

    /// <summary>
    /// Prints a port read: the typed port wrapper when one is declared for the constant port, an
    /// <c>InN(port)</c> call with a <c>no device</c> comment when the dumped machine had no device
    /// registered for the port, and the raw <c>InN(port)</c> call otherwise. A program built
    /// without a machine prints no comment.
    /// </summary>
    private CSharpFragment PortRead(PortReadCallNode node) {
        CSharpFragment portFragment = Expr(node.CallNode.Arguments[0]);
        if (portFragment.Constant is ConstantNode constant
            && PortWrapperRegistry.TryFind((ushort)constant.Value, node.Width, PortDirection.In,
                out string? call)) {
            return Atomic($"{call}()", node.DataType);
        }
        string port = Cast(DataType.UINT16, portFragment) + NoDeviceComment(portFragment);
        return Atomic($"In{(int)node.Width}({port})", node.DataType);
    }

    public CSharpFragment VisitRegisterNode(RegisterNode node) {
        if (LocalReads is not null && LocalReads.TryGetValue(node, out SubstitutedRead? read)) {
            return Atomic(read.Text, read.Type ?? node.DataType);
        }
        if (ParameterReads is not null && ParameterReads.TryGetValue(node, out string? parameter)) {
            return Atomic(parameter, node.DataType);
        }
        return Atomic(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex), node.DataType);
    }

    public CSharpFragment VisitSegmentRegisterNode(SegmentRegisterNode node) {
        if (LocalReads is not null && LocalReads.TryGetValue(node, out SubstitutedRead? read)) {
            return Atomic(read.Text, read.Type ?? node.DataType);
        }
        if (ParameterReads is not null && ParameterReads.TryGetValue(node, out string? parameter)) {
            return Atomic(parameter, node.DataType);
        }
        return Atomic(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex), DataType.UINT16);
    }

    public CSharpFragment VisitSegmentedPointer(SegmentedPointerNode node) {
        if (FramePlan is { } framePlan
            && framePlan.Names.TryGetValue(node, out string? frameSlot)) {
            return Atomic($"{framePlan.VariableName}.{frameSlot}", node.DataType);
        }
        if (IrGlobalAccesses.TryAccess(node, out ConstantMemoryAccess global)) {
            if (GlobalClasses is not null
                && GlobalClasses.TryTarget(node, out GlobalAccessClassification.Target? target)) {
                return Atomic($"{target.Class.Name}.{IrGlobalAccesses.PropertyName(target.Key.Offset, target.Key.Width)}",
                    node.DataType);
            }
            return Atomic($"{IrGlobalAccesses.ClassName(global.Segment)}.{IrGlobalAccesses.PropertyName(global)}",
                node.DataType);
        }
        if (GlobalClasses is not null
            && GlobalClasses.TryTableTarget(node, out GlobalAccessClassification.TableTarget? table)) {
            return Atomic($"{table.Class.Name}.{IrGlobalAccesses.PropertyName(table.Property.Base, table.Property.Width)}[{Expr(table.Index)}]",
                node.DataType);
        }
        string indexer = ToMemoryIndexer(node.DataType);
        CSharpFragment offset = Expr(node.Offset);
        if (node.Segment is SegmentRegisterNode
                { RegisterIndex: (int)SegmentRegisterIndex.SsIndex }) {
            return Atomic($"Ss{indexer}[{MemoryOffset(offset)}]", node.DataType);
        }
        string segment = Expr(node.Segment);
        return Atomic($"{indexer}[{segment}, {MemoryOffset(offset)}]", node.DataType);
    }

    public CSharpFragment VisitAbsolutePointerNode(AbsolutePointerNode node) =>
        Atomic($"{ToMemoryIndexer(node.DataType)}[{Cast(DataType.UINT32, Expr(node.AbsoluteAddress))}]", node.DataType);

    public CSharpFragment VisitConditionalValueNode(ConditionalValueNode node) {
        if (node.Condition is ConditionalValueNode) {
            throw Unsupported(node);
        }
        CSharpFragment whenTrue = Expr(node.WhenTrue);
        CSharpFragment whenFalse = Expr(node.WhenFalse);
        string text = $"{Parenthesize(Expr(node.Condition), ConditionalPrecedence + 1)} ? "
            + $"{whenTrue} : {whenFalse}";
        return new CSharpFragment(text, SelectType(node, whenTrue, whenFalse), ConditionalPrecedence);
    }

    /// <summary>The C# type a printed select may claim: <see cref="ValueNode.DataType"/> when the
    /// conditional already evaluates to it - a <see cref="DataType.BOOL"/> select, or two
    /// non-literal arms of exactly that type - and <see langword="null"/> otherwise, so an
    /// assignment casts the int-promoted or differently typed C# result (C# types
    /// <c>c ? 1 : 0</c> as <c>int</c>, and a literal arm prints no type).</summary>
    private static DataType? SelectType(ConditionalValueNode node, CSharpFragment whenTrue,
        CSharpFragment whenFalse) {
        if (node.DataType == DataType.BOOL) {
            return DataType.BOOL;
        }
        if (whenTrue.Constant is null && whenFalse.Constant is null
            && whenTrue.Type == node.DataType && whenFalse.Type == node.DataType) {
            return node.DataType;
        }
        return null;
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
            ConstantNode converted = new(node.DataType, constant.ConvertAsCSharpCast(node.DataType));
            if (node.DataType == DataType.BOOL) {
                return ConstantLiteral(converted);
            }
            // A literal alone is an int to C#: keep the cast so the conversion still pins the type
            // where the argument resolves an overload.
            return new CSharpFragment(Cast(node.DataType, ConstantLiteral(constant)),
                node.DataType, CastPrecedence) { Constant = converted };
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

    public CSharpFragment VisitFlagRegisterNode(FlagRegisterNode node) {
        if (node.DataType.BitWidth == BitWidth.WORD_16) {
            return Atomic("FlagRegister16", DataType.UINT16);
        }
        return Atomic("FlagRegister", DataType.UINT32);
    }

    public CSharpFragment VisitCpuFlagNode(CpuFlagNode node) {
        if (ParameterReads is not null && ParameterReads.TryGetValue(node, out string? parameter)) {
            return Atomic(parameter, DataType.BOOL);
        }
        return Atomic(FlagName(node), DataType.BOOL);
    }

    private static string FlagName(CpuFlagNode node) => node.FlagMask switch {
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
    };

    public CSharpFragment VisitInstructionFieldNode(InstructionFieldNode node) => Expr(node.ResolvedNode);
    public CSharpFragment VisitVariableReferenceNode(VariableReferenceNode node) =>
        Atomic(node is MethodScopedVariableReferenceNode ? node.VariableName : LocalVariableName(node.VariableName),
            node.DataType);
    public CSharpFragment VisitVariableDeclarationNode(VariableDeclarationNode node) =>
        $"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)} = {Cast(node.DataType, Expr(node.Initializer))}";
    public CSharpFragment VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        $"new SegmentedAddress({Cast(DataType.UINT16, Expr(node.Segment))}, {Cast(DataType.UINT16, Expr(node.Offset))})";

    private string EmitAssignmentRight(BinaryOperationNode assignment) {
        if (LocalReads is not null && LocalReads.TryGetValue(assignment.Left, out SubstitutedRead? read)
            && read.Type is { } localType) {
            // The target is a local of another type than its register: the right side casts to the
            // local's type, and a conversion from that type inside it is the identity.
            ValueNode right = SameWidthSignRoundTripOperand(localType, assignment.Right) ?? assignment.Right;
            return Cast(localType, Expr(right));
        }
        CSharpFragment expression = Expr(assignment.Right);
        return Cast(assignment.Left.DataType, expression);
    }

    private static string ToMemoryIndexer(DataType dataType) => dataType.BitWidth switch {
        BitWidth.BYTE_8 => dataType.Signed ? "Int8" : "UInt8",
        BitWidth.WORD_16 => dataType.Signed ? "Int16" : "UInt16",
        BitWidth.DWORD_32 or BitWidth.BOOL_1 => dataType.Signed ? "Int32" : "UInt32",
        _ => throw new NotSupportedException($"Unsupported memory data type {dataType}.")
    };

    internal static string ToCSharpType(DataType dataType) {
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
        if (expression.Constant is ConstantNode constant) {
            // The checked cast compiles only on an in-range literal, and it pins the type where the
            // argument resolves an overload. The original literal is kept when it fits (it carries the
            // operand's own width padding); out of range, the converted value of the target type prints.
            string literal = Fits(constant, dataType)
                ? ConstantLiteral(constant).Text
                : ConstantLiteral(new ConstantNode(dataType,
                    constant.ConvertAsCSharpCast(dataType))).Text;
            return $"({ToCSharpType(dataType)}){literal}";
        }

        string targetType = ToCSharpType(dataType);
        string inner = Parenthesize(expression, CastPrecedence);
        return $"({targetType}){inner}";
    }

    /// <summary>Whether <paramref name="constant"/>'s literal value is inside
    /// <paramref name="dataType"/>'s range, so a checked cast on it compiles.</summary>
    private static bool Fits(ConstantNode constant, DataType dataType) {
        int bits = (int)dataType.BitWidth;
        if (constant.DataType.Signed && constant.SignedValue < 0) {
            if (!dataType.Signed) {
                return false;
            }
            long minimum = bits == 64 ? long.MinValue : -(1L << (bits - 1));
            return constant.SignedValue >= minimum;
        }
        ulong maximum = dataType.Signed ? (1UL << (bits - 1)) - 1 : ConstantNode.Mask(dataType.BitWidth);
        return constant.Value <= maximum;
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
    /// Renders a memory-indexer offset. An atomic offset (a constant or a single register/variable)
    /// is emitted directly; a compound offset is wrapped in an explicit cast: <c>(ushort)(...)</c>
    /// when it is 16-bit address arithmetic that must wrap at 64 KiB, <c>(uint)(...)</c> when its
    /// type is wider than 16 bits so the offset keeps its full value and binds the indexer's
    /// <c>uint</c> overload (which performs the CPU's segment-limit check).
    /// </summary>
    private static string MemoryOffset(CSharpFragment offset) {
        if (offset.Precedence >= CSharpFragment.AtomicPrecedence) {
            return offset.Text;
        }
        string type = offset.Type?.BitWidth > BitWidth.WORD_16 ? "uint" : "ushort";
        return $"({type})({offset.Text})";
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
        if (node.SingleEntry) {
            string delegateName = TypedSignatures?.DelegateNameOf(node.MethodName) ?? node.MethodName;
            return new CSharpFragment(GeneratedOverrideNames.EntryFieldNameOf(delegateName));
        }
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
    public CSharpFragment VisitMultiplicationNode(MultiplicationNode node) => throw NotAnExpression(node);

    public CSharpFragment VisitAluOperationNode(AluOperationNode node) => node.Call.Accept(this);
    public CSharpFragment VisitAluStatementNode(AluStatementNode node) => node.Call.Accept(this);

    /// <summary>
    /// Prints a port write: the typed port wrapper when one is declared for the constant port, an
    /// <c>OutN(port, value)</c> call with a <c>no device</c> comment when the dumped machine had no
    /// device registered for the port, and the raw <c>OutN(port, value)</c> call otherwise. A
    /// program built without a machine prints no comment.
    /// </summary>
    private CSharpFragment PortWrite(PortWriteCallNode node) {
        CSharpFragment portFragment = Expr(node.Arguments[0]);
        string value = Cast(PortValueType(node.Width), Expr(node.Arguments[1]));
        if (portFragment.Constant is ConstantNode constant
            && PortWrapperRegistry.TryFind((ushort)constant.Value, node.Width, PortDirection.Out,
                out string? call)) {
            return new CSharpFragment($"{call}({value})");
        }
        string port = Cast(DataType.UINT16, portFragment) + NoDeviceComment(portFragment);
        return new CSharpFragment($"Out{(int)node.Width}({port}, {value})");
    }

    private string NoDeviceComment(CSharpFragment portFragment) {
        if (IoPorts is not null && portFragment.Constant is ConstantNode constant
            && !IoPorts.ContainsKey((ushort)constant.Value)) {
            return " /* no device */";
        }
        return string.Empty;
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
    public CSharpFragment VisitLocalDeclarationNode(LocalDeclarationNode node) {
        if (node.Initializer is { } initializer) {
            return $"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)} = {Cast(node.DataType, Expr(initializer))}";
        }
        return $"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)}";
    }
    public CSharpFragment VisitRegisterLocalNode(RegisterLocalNode node) =>
        Atomic(node.VariableName, node.DataType);
    public CSharpFragment VisitRegisterWriteBackNode(RegisterWriteBackNode node) =>
        new CSharpFragment($"{Expr(node.Register)} = {Cast(node.Register.DataType, Expr(node.Local))}",
            node.Register.DataType, AssignmentPrecedence);

    private readonly record struct ClrTypeInfo(string Name, DataType? DataType);
}
