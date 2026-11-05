namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ranges;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The unsigned range a register version can hold, from its definitions (a constant, a mask, a
/// shift or an offset of a defined range, the union of a phi's operands) and from the conditions
/// that dominate a use (a <c>cmp bx,5; ja</c> whose taken target has the branch as its only
/// predecessor and dominates the use refines the compared version). A range is about one
/// register width: a narrower read of a wider proven range keeps it when its Max fits the
/// narrower width, a wider read gets <see cref="IrValueRange.Full(int)"/>.
/// </summary>
internal sealed class IrValueRanges {
    private readonly IrSsa _ssa;
    private readonly DominatorTree<IrBlock> _dominators;
    private readonly List<(IrBlock Source, IrBlock Target, IrBranch Branch, bool Negated)>
        _edges = [];
    private readonly Dictionary<(int Version, IrBlock Block, int Offset, int Width), IrValueRange>
        _memo = new();
    private readonly HashSet<(int Version, IrBlock Block, int Offset, int Width)> _visiting = [];

    private IrValueRanges(IrMethod method, IrSsa ssa) {
        _ssa = ssa;
        BlockGraph<IrBlock> graph = method.ToStructuringGraph();
        _dominators = DominatorTreeBuilder.BuildFromEntries(graph);
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrBranch branch) {
                continue;
            }
            CollectEdge(block, branch, branch.WhenTrue, false, graph);
            CollectEdge(block, branch, branch.WhenFalse, true, graph);
        }
    }

    /// <summary>Computes the analysis: the dominator tree and the single-predecessor branch edges
    /// are built eagerly; every version's range is resolved lazily at the first use.</summary>
    public static IrValueRanges Compute(IrMethod method, IrSsa ssa) => new(method, ssa);

    private void CollectEdge(IrBlock block, IrBranch branch, IrEdge edge, bool negated,
        BlockGraph<IrBlock> graph) {
        if (graph.EntryBlocks.Contains(edge.Target)) {
            return;
        }
        if (graph.GetPredecessors(edge.Target) is [IrBlock only] && ReferenceEquals(only, block)) {
            _edges.Add((block, edge.Target, branch, negated));
        }
    }

    /// <summary>The range of <paramref name="register"/> (its own width: 8, 16 or 32 bits) as read
    /// by the statement at <paramref name="statementIndex"/> of <paramref name="block"/>, or by the
    /// block's terminator when the index is the count of statements.</summary>
    public IrValueRange RangeAt(RegisterNode register, IrBlock block, int statementIndex) {
        if (SpanOf(register) is not { } span) {
            return IrValueRange.Full(WidthOf(register.DataType.BitWidth));
        }
        int version = _ssa.VersionBefore(block, statementIndex, span.Value);
        return RangeOf(version, block, span.Offset, span.Width);
    }

    /// <summary>The (SSA value, bit offset inside it, bit width) a register node covers: the low or
    /// high byte for an 8-bit register, the whole word or double word otherwise, the whole register
    /// for a segment register.</summary>
    private static (IrSsaValue Value, int Offset, int Width)? SpanOf(RegisterNode register) {
        if (register is SegmentRegisterNode segment) {
            return ((IrSsaValue)((int)IrSsaValue.Es + segment.RegisterIndex), 0, 16);
        }
        if (register.RegisterIndex > 7) {
            return null;
        }
        return register.DataType.BitWidth switch {
            BitWidth.BYTE_8 => ((IrSsaValue)(int)
                    UInt8HighLowRegistersIndexer.ComputeRegisterIndexInArray((uint)register.RegisterIndex),
                UInt8HighLowRegistersIndexer.IsHigh((uint)register.RegisterIndex) ? 8 : 0, 8),
            BitWidth.WORD_16 => ((IrSsaValue)register.RegisterIndex, 0, 16),
            BitWidth.DWORD_32 => ((IrSsaValue)register.RegisterIndex, 0, 32),
            _ => null
        };
    }

    private static int WidthOf(BitWidth bitWidth) => bitWidth switch {
        BitWidth.BYTE_8 => 8,
        BitWidth.WORD_16 => 16,
        BitWidth.DWORD_32 => 32,
        BitWidth.QWORD_64 => 64,
        _ => 32
    };

    private static ulong Mask(int bits) => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;

    /// <summary>The range of <paramref name="version"/> as used inside <paramref name="block"/> at
    /// bit span (<paramref name="offset"/>, <paramref name="width"/>): the definition's range
    /// intersected with every dominating branch refinement. A recursion that comes back to the
    /// same key in progress gives <see cref="IrValueRange.Full(int)"/>.</summary>
    private IrValueRange RangeOf(int version, IrBlock block, int offset, int width) {
        if (version < 0) {
            return IrValueRange.Full(width);
        }
        (int Version, IrBlock Block, int Offset, int Width) key = (version, block, offset, width);
        if (_memo.TryGetValue(key, out IrValueRange cached)) {
            return cached;
        }
        if (!_visiting.Add(key)) {
            return IrValueRange.Full(width);
        }
        IrValueRange result = DefinitionRange(version, offset, width)
            .Intersect(ConditionRange(version, block, offset, width));
        _visiting.Remove(key);
        _memo[key] = result;
        return result;
    }

    /// <summary>Projects a range known at bit span (<paramref name="offset"/>, <paramref name="width"/>)
    /// onto the queried span: an exact or a narrower same-offset read keeps it when its Max fits,
    /// everything else is <see cref="IrValueRange.Full(int)"/>.</summary>
    private static IrValueRange Project(IrValueRange range, int offset, int width,
        int queryOffset, int queryWidth) {
        if (offset != queryOffset) {
            return IrValueRange.Full(queryWidth);
        }
        if (width == queryWidth || (width > queryWidth && range.Max <= Mask(queryWidth))) {
            return range;
        }
        return IrValueRange.Full(queryWidth);
    }

    /// <summary>Resizes an expression's range to <paramref name="targetBits"/>: a widening keeps
    /// it, a narrowing keeps it when its Max fits.</summary>
    private static IrValueRange Resize(IrValueRange range, int sourceBits, int targetBits) {
        if (targetBits >= sourceBits || range.Max <= Mask(targetBits)) {
            return range;
        }
        return IrValueRange.Full(targetBits);
    }

    /// <summary>The range <paramref name="version"/>'s definition gives it, projected onto the
    /// queried span.</summary>
    private IrValueRange DefinitionRange(int version, int offset, int width) {
        IrSsaVersionInfo info = _ssa.Info(version);
        switch (info.Kind) {
            case IrSsaDefinitionKind.Phi:
                IrSsaPhi? phi = _ssa.Phis.FirstOrDefault(candidate => candidate.Result == version);
                if (phi is null) {
                    return IrValueRange.Full(width);
                }
                IrValueRange union = new(1, 0);
                foreach ((IrBlock predecessor, int operand) in phi.Operands) {
                    union = union.Union(RangeOf(operand, predecessor, offset, width));
                }
                return phi.HasEntryOperand ? IrValueRange.Full(width) : union;
            case IrSsaDefinitionKind.Definition or IrSsaDefinitionKind.PartialDefinition:
                if (_ssa.DefiningStatement(version) is not { } statement
                    || statement.Node is not BinaryOperationNode {
                        BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode left,
                        Right: ValueNode right }
                    || SpanOf(left) is not { } span
                    || span.Value != info.Value) {
                    return IrValueRange.Full(width);
                }
                IrValueRange defined = Resize(
                    ExpressionRange(right, info.Block, statement, WidthOf(right.DataType.BitWidth)),
                    WidthOf(right.DataType.BitWidth), span.Width);
                return Project(defined, span.Offset, span.Width, offset, width);
            default:
                return IrValueRange.Full(width);
        }
    }

    /// <summary>The range of <paramref name="node"/>, at its own width, with the versions the
    /// defining <paramref name="statement"/> of <paramref name="block"/> reads.</summary>
    private IrValueRange ExpressionRange(ValueNode node, IrBlock block, IrStatement statement,
        int width) {
        if (ConstantOf(node) is { } constant) {
            return IrValueRange.Constant(constant & Mask(width));
        }
        switch (node) {
            case TypeConversionNode conversion:
                if (conversion.DataType.Signed) {
                    return IrValueRange.Full(width);
                }
                IrValueRange inner = ExpressionRange(conversion.Value, block, statement,
                    WidthOf(conversion.Value.DataType.BitWidth));
                return Resize(inner, WidthOf(conversion.Value.DataType.BitWidth), width);
            case RegisterNode source:
                if (SpanOf(source) is { } sourceSpan) {
                    int sourceVersion = _ssa.VersionUsed(statement, sourceSpan.Value);
                    return RangeOf(sourceVersion, block, sourceSpan.Offset, sourceSpan.Width);
                }
                return IrValueRange.Full(width);
            case AluOperationNode alu:
                return AluRange(alu, block, statement, width);
            case BinaryOperationNode binary:
                return BinaryRange(binary, block, statement, width);
            case InstructionFieldNode { ResolvedNode: ValueNode resolved }:
                return ExpressionRange(resolved, block, statement, width);
            default:
                return IrValueRange.Full(width);
        }
    }

    private IrValueRange OperandRange(IVisitableAstNode node, IrBlock block, IrStatement statement,
        int width) {
        if (node is not ValueNode value) {
            return IrValueRange.Full(width);
        }
        return Resize(ExpressionRange(value, block, statement, WidthOf(value.DataType.BitWidth)),
            WidthOf(value.DataType.BitWidth), width);
    }

    private IrValueRange AluRange(AluOperationNode alu, IrBlock block, IrStatement statement,
        int width) {
        IReadOnlyList<IVisitableAstNode> arguments = alu.Call.CallNode.Arguments;
        if (arguments.Count == 0) {
            return IrValueRange.Full(width);
        }
        IrValueRange operand = OperandRange(arguments[0], block, statement, width);
        ulong? count = arguments.Count > 1 ? ConstantOf(arguments[1]) : null;
        switch (alu.Call.Operation) {
            case AluOperation.And when count is { } mask:
                return new IrValueRange(0, mask & Mask(width));
            case AluOperation.Shr when count is { } shift:
                int logical = (int)(shift & 31);
                return new IrValueRange(operand.Min >> logical, operand.Max >> logical);
            case AluOperation.Shl when count is { } shift:
                int shiftAmount = (int)(shift & 31);
                if (shiftAmount < width && operand.Max <= Mask(width) >> shiftAmount) {
                    return new IrValueRange(operand.Min << shiftAmount,
                        operand.Max << shiftAmount);
                }
                return IrValueRange.Full(width);
            case AluOperation.Add:
                if (count is { } addend && addend <= Mask(width)
                    && operand.Max <= Mask(width) - addend) {
                    return new IrValueRange(operand.Min + addend, operand.Max + addend);
                }
                return IrValueRange.Full(width);
            case AluOperation.Inc:
                if (operand.Max < Mask(width)) {
                    return new IrValueRange(operand.Min + 1, operand.Max + 1);
                }
                return IrValueRange.Full(width);
            case AluOperation.Sub:
                if (count is { } subtrahend && operand.Min >= subtrahend) {
                    return new IrValueRange(operand.Min - subtrahend, operand.Max - subtrahend);
                }
                return IrValueRange.Full(width);
            case AluOperation.Dec:
                if (operand.Min >= 1) {
                    return new IrValueRange(operand.Min - 1, operand.Max - 1);
                }
                return IrValueRange.Full(width);
            default:
                return IrValueRange.Full(width);
        }
    }

    private IrValueRange BinaryRange(BinaryOperationNode binary, IrBlock block,
        IrStatement statement, int width) {
        ulong? constant = ConstantOf(binary.Right);
        IrValueRange operand = OperandRange(binary.Left, block, statement, width);
        return binary.BinaryOperation switch {
            BinaryOperation.BITWISE_AND when constant is { } mask =>
                new IrValueRange(0, mask & Mask(width)),
            BinaryOperation.RIGHT_SHIFT when constant is { } shift
                && !binary.DataType.Signed && !binary.Left.DataType.Signed =>
                new IrValueRange(operand.Min >> (int)(shift & 31),
                    operand.Max >> (int)(shift & 31)),
            BinaryOperation.LEFT_SHIFT when constant is { } shift =>
                LeftShift(operand, shift, width),
            BinaryOperation.PLUS when constant is { } addend =>
                AddRange(operand, addend, width),
            BinaryOperation.MINUS when constant is { } subtrahend =>
                SubtractRange(operand, subtrahend, width),
            _ => IrValueRange.Full(width)
        };
    }

    private IrValueRange LeftShift(IrValueRange operand, ulong shift, int width) {
        if ((shift & 31) < (ulong)width && operand.Max <= Mask(width) >> (int)(shift & 31)) {
            return new IrValueRange(operand.Min << (int)(shift & 31),
                operand.Max << (int)(shift & 31));
        }
        return IrValueRange.Full(width);
    }

    private IrValueRange AddRange(IrValueRange operand, ulong addend, int width) {
        if (addend <= Mask(width) && operand.Max <= Mask(width) - addend) {
            return new IrValueRange(operand.Min + addend, operand.Max + addend);
        }
        return IrValueRange.Full(width);
    }

    private IrValueRange SubtractRange(IrValueRange operand, ulong subtrahend, int width) {
        if (operand.Min >= subtrahend) {
            return new IrValueRange(operand.Min - subtrahend, operand.Max - subtrahend);
        }
        return IrValueRange.Full(width);
    }

    /// <summary>The conjunction of what every dominating single-predecessor branch edge implies
    /// about <paramref name="version"/> at the queried span.</summary>
    private IrValueRange ConditionRange(int version, IrBlock use, int offset, int width) {
        IrSsaValue value = _ssa.Info(version).Value;
        IrValueRange result = IrValueRange.Full(width);
        foreach ((IrBlock source, IrBlock target, IrBranch branch, bool negated) in _edges) {
            if (!_dominators.Dominates(target, use)
                || CompareOf(branch.Condition) is not { } compare
                || compare.Value != value
                || _ssa.VersionBefore(source, source.Statements.Count, compare.Value) != version) {
                continue;
            }
            IrValueRange current = Project(result, offset, width, compare.Offset, compare.Width);
            IrValueRange implied = ImpliedRange(compare, negated, current);
            result = result.Intersect(
                Project(implied, compare.Offset, compare.Width, offset, width));
        }
        return result;
    }

    /// <summary>A compare of one register with one constant inside a branch condition, with the
    /// register normalized as the left operand. <see langword="null"/> for every other shape,
    /// including a relation under a signed conversion.</summary>
    private (IrSsaValue Value, int Offset, int Width, BinaryOperation Operation, ulong Constant)?
        CompareOf(ValueNode condition) {
        if (condition is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.LESS_THAN or BinaryOperation.LESS_THAN_OR_EQUAL
                    or BinaryOperation.GREATER_THAN or BinaryOperation.GREATER_THAN_OR_EQUAL
                    or BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL,
                Left: ValueNode left, Right: ValueNode right } comparison) {
            return null;
        }
        if (UnsignedOperand(left) is RegisterNode leftRegister
            && ConstantOf(right) is { } rightConstant
            && SpanOf(leftRegister) is { } leftSpan) {
            return (leftSpan.Value, leftSpan.Offset, leftSpan.Width,
                comparison.BinaryOperation, rightConstant);
        }
        if (UnsignedOperand(right) is RegisterNode rightRegister
            && ConstantOf(left) is { } leftConstant
            && SpanOf(rightRegister) is { } rightSpan) {
            return (rightSpan.Value, rightSpan.Offset, rightSpan.Width,
                Swap(comparison.BinaryOperation), leftConstant);
        }
        return null;
    }

    /// <summary>The node under zero or more unsigned conversions; <see langword="null"/> when a
    /// signed conversion is stripped, so a signed relation refines nothing.</summary>
    private static IVisitableAstNode? UnsignedOperand(IVisitableAstNode node) =>
        node switch {
            TypeConversionNode conversion =>
                conversion.DataType.Signed ? null : UnsignedOperand(conversion.Value),
            _ => node
        };

    private static BinaryOperation Swap(BinaryOperation operation) => operation switch {
        BinaryOperation.LESS_THAN => BinaryOperation.GREATER_THAN,
        BinaryOperation.LESS_THAN_OR_EQUAL => BinaryOperation.GREATER_THAN_OR_EQUAL,
        BinaryOperation.GREATER_THAN => BinaryOperation.LESS_THAN,
        BinaryOperation.GREATER_THAN_OR_EQUAL => BinaryOperation.LESS_THAN_OR_EQUAL,
        _ => operation
    };

    private static BinaryOperation Negate(BinaryOperation operation) => operation switch {
        BinaryOperation.LESS_THAN => BinaryOperation.GREATER_THAN_OR_EQUAL,
        BinaryOperation.LESS_THAN_OR_EQUAL => BinaryOperation.GREATER_THAN,
        BinaryOperation.GREATER_THAN => BinaryOperation.LESS_THAN_OR_EQUAL,
        BinaryOperation.GREATER_THAN_OR_EQUAL => BinaryOperation.LESS_THAN,
        BinaryOperation.EQUAL => BinaryOperation.NOT_EQUAL,
        _ => BinaryOperation.EQUAL
    };

    /// <summary>The interval the relation implies at the compare's own span. A <c>!=</c> refines
    /// only when the constant is a bound of <paramref name="current"/>, the range the version
    /// already has at that span.</summary>
    private static IrValueRange ImpliedRange(
        (IrSsaValue Value, int Offset, int Width, BinaryOperation Operation, ulong Constant) compare,
        bool negated, IrValueRange current) {
        BinaryOperation operation = negated ? Negate(compare.Operation) : compare.Operation;
        ulong constant = compare.Constant;
        ulong mask = Mask(compare.Width);
        switch (operation) {
            case BinaryOperation.LESS_THAN:
                if (constant == 0) {
                    return new IrValueRange(1, 0);
                }
                return new IrValueRange(0, Math.Min(constant - 1, mask));
            case BinaryOperation.LESS_THAN_OR_EQUAL:
                return new IrValueRange(0, Math.Min(constant, mask));
            case BinaryOperation.GREATER_THAN:
                if (constant >= mask) {
                    return new IrValueRange(1, 0);
                }
                return new IrValueRange(constant + 1, mask);
            case BinaryOperation.GREATER_THAN_OR_EQUAL:
                if (constant > mask) {
                    return new IrValueRange(1, 0);
                }
                return new IrValueRange(constant, mask);
            case BinaryOperation.EQUAL:
                if (constant <= mask) {
                    return IrValueRange.Constant(constant);
                }
                return new IrValueRange(1, 0);
            case BinaryOperation.NOT_EQUAL:
                if (current.IsEmpty) {
                    return IrValueRange.Full(compare.Width);
                }
                if (constant == current.Min) {
                    return new IrValueRange(constant + 1, current.Max);
                }
                if (constant == current.Max) {
                    return new IrValueRange(current.Min, constant - 1);
                }
                return IrValueRange.Full(compare.Width);
            default:
                return IrValueRange.Full(compare.Width);
        }
    }

    /// <summary>The constant a node stands for, evaluated with its conversions (a signed source
    /// type sign-extends, the outer type then truncates), or <see langword="null"/>.</summary>
    private static ulong? ConstantOf(IVisitableAstNode node) =>
        ConstantEvaluation.TryEvaluate(node, _ => null, out ulong value) ? value : null;
}
