namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Linq;

/// <summary>
/// Joins two provably adjacent 16-bit RAM accesses into one 32-bit access.
/// <para>The <em>load</em> form rewrites the <c>(uint)low | ((uint)high &lt;&lt; 16)</c>
/// expression a double-precision pair or a manual pack emits into a single widened read of the
/// low pointer: both reads evaluate in the same statement, so no observer can sit between them.</para>
/// <para>The <em>store</em> form merges two consecutive effective word stores at offsets
/// <c>n</c> and <c>n + 2</c> into one dword store at the second statement; the first value is
/// re-evaluated there under <see cref="IrSsa.SameVersions"/>, so an intervening write or a value
/// change refuses the merge.</para>
/// <para>An address pair is proven only by the alias analysis: both pointers are
/// <see cref="SegmentedPointerNode"/>s with 16-bit unsigned segment and offset expressions, each
/// offset resolves to a constant word with <c>high == low + 2</c> and no wrap, the segments are
/// the same known RAM value read at the same version, and the widened four-byte range stays
/// under the device mappings (<see cref="IrMemoryAliasAnalysis.IsKnownRam"/>). Anything dynamic,
/// wrapped, signed or mapped refuses: a merge would then read or write bytes the original code
/// never touched.</para>
/// </summary>
internal static class IrJoinedMemory {
    /// <summary>Applies both forms over <paramref name="method"/>; returns the number of joined
    /// statements. The SSA must be rebuilt by the caller when the result is not zero.</summary>
    public static int Apply(IrMethod method, IrSsa ssa) {
        IrMemoryAliasAnalysis alias = new(ssa);
        HashSet<ICfgNode> pinnedOrigins = IrInstructionIdioms.PinnedOriginsOf(method);
        int joined = 0;
        foreach (IrBlock block in method.Blocks) {
            joined += JoinBlock(block, alias, ssa, pinnedOrigins);
        }
        return joined;
    }

    private static int JoinBlock(IrBlock block, IrMemoryAliasAnalysis alias, IrSsa ssa,
            HashSet<ICfgNode> pinnedOrigins) {
        List<IrStatement> statements = block.Statements;
        int joined = 0;
        for (int i = 0; i < statements.Count; i++) {
            IrStatement statement = statements[i];
            IVisitableAstNode node = RewriteReads(statement.Node, statement, alias);
            if (!ReferenceEquals(node, statement.Node)) {
                IrStatement rewritten = IrStatement.Create(node, statement.Origin);
                statements[i] = rewritten;
                if (ssa.HasSite(statement)) {
                    ssa.Replace(statement, rewritten);
                }
                joined++;
            }
            if (!IsWordStore(statements[i])) {
                continue;
            }
            int second = IrInstructionIdioms.NextEffective(statements, i + 1);
            if (second < 0 || !IsWordStore(statements[second])
                || IrInstructionIdioms.CrossesPinnedOrigin(statements, i, second, pinnedOrigins)) {
                continue;
            }
            if (JoinStore(block, i, second, alias, ssa)) {
                statements.RemoveAt(i);
                i--;
                joined++;
            }
        }
        return joined;
    }

    // ------------------------------------------------------------------
    // Address proof
    // ------------------------------------------------------------------

    /// <summary>Whether <paramref name="low"/> and <paramref name="high"/> are provably the two
    /// halves of one four-byte RAM range, evaluated at <paramref name="statement"/>.</summary>
    private static bool ProvenPair(SegmentedPointerNode low, SegmentedPointerNode high,
            IrStatement statement, IrMemoryAliasAnalysis alias) {
        // Only 16-bit unsigned segment and offset expressions: a wider or signed address
        // expression could carry a value the word accesses never had.
        if (low.DataType != DataType.UINT16 || high.DataType != DataType.UINT16
            || low.Segment.DataType != DataType.UINT16 || high.Segment.DataType != DataType.UINT16
            || low.Offset.DataType != DataType.UINT16 || high.Offset.DataType != DataType.UINT16) {
            return false;
        }
        if (ConstantEvaluation.Resolved(low.Offset) is not { } lowOffset
            || ConstantEvaluation.Resolved(high.Offset) is not { } highOffset
            || lowOffset.Value > 0xFFFC || highOffset.Value != lowOffset.Value + 2) {
            // Adjacency without wrap: the widened range must not cross the 64 KiB boundary.
            return false;
        }
        IrMemoryAccess lowAccess = alias.OfPointer(low, statement);
        IrMemoryAccess highAccess = alias.OfPointer(high, statement);
        // Same segment: the same known value through the same register identity and version.
        if (lowAccess.SegmentValue is not { } segment
            || highAccess.SegmentValue is not { } otherSegment || segment != otherSegment
            || lowAccess.SegmentRegister != highAccess.SegmentRegister
            || lowAccess.SegmentVersion != highAccess.SegmentVersion) {
            return false;
        }
        // The two word accesses and the widened four-byte access must all stay in known RAM:
        // a device read can observe, a device store can be ignored.
        return alias.IsKnownRam(lowAccess) && alias.IsKnownRam(highAccess)
            && alias.IsKnownRam(lowAccess with { Width = 4 });
    }

    /// <summary>Whether the statement may be folded around at all: never faulting, never
    /// delivering an interrupt, never touching a token other than the memory a store writes.</summary>
    private static bool PlainStatement(IrStatement statement) =>
        !statement.Effects.MayFault && !statement.Effects.DeliversInterrupts
        && ((statement.Effects.TokenReads | statement.Effects.TokenWrites) & ~IrTokens.Memory)
            == IrTokens.None;

    // ------------------------------------------------------------------
    // Load form
    // ------------------------------------------------------------------

    /// <summary><paramref name="node"/> with the joined loads of every read position applied:
    /// write targets - an assignment's left, the halves of a multiplication, division or swap, a
    /// write-back's register, a service call's results - are never descended.</summary>
    private static IVisitableAstNode RewriteReads(IVisitableAstNode node, IrStatement statement,
            IrMemoryAliasAnalysis alias) {
        if (!PlainStatement(statement)) {
            return node;
        }
        return node switch {
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign
                => JoinedValue(assign.Right, statement, alias) is { } right
                    && !ReferenceEquals(right, assign.Right)
                    ? assign with { Right = right }
                    : node,
            VariableDeclarationNode declaration
                => JoinedValue(declaration.Initializer, statement, alias) is { } initializer
                    && !ReferenceEquals(initializer, declaration.Initializer)
                    ? declaration with { Initializer = initializer }
                    : node,
            MultiplicationNode multiplication
                => RewriteOperands(multiplication.Left, multiplication.Right, statement, alias)
                    is { } operands
                    ? multiplication with { Left = operands.Left, Right = operands.Right }
                    : node,
            DivisionNode division
                => RewriteOperands(division.Division.Dividend, division.Division.Divisor,
                        statement, alias)
                    is { } operands
                    ? division with { Division = division.Division with {
                        Dividend = operands.Left, Divisor = operands.Right } }
                    : node,
            TupleSwapNode or RegisterWriteBackNode or OpaqueNode or InstructionNode
                or PreTestLoopNode or DoWhileNode or InfiniteLoopNode or SwitchNode => node,
            _ => IrAstChildren.MapChildren(node,
                child => RewriteReads(child, statement, alias))
        };
    }

    private static (ValueNode Left, ValueNode Right)? RewriteOperands(ValueNode left,
            ValueNode right, IrStatement statement, IrMemoryAliasAnalysis alias) {
        ValueNode newLeft = JoinedValue(left, statement, alias) ?? left;
        ValueNode newRight = JoinedValue(right, statement, alias) ?? right;
        return ReferenceEquals(newLeft, left) && ReferenceEquals(newRight, right)
            ? null
            : (newLeft, newRight);
    }

    /// <summary><paramref name="node"/> with joined loads applied bottom-up, then itself matched
    /// as a pack expression.</summary>
    private static ValueNode? JoinedValue(ValueNode node, IrStatement statement,
            IrMemoryAliasAnalysis alias) {
        ValueNode rebuilt = node switch {
            BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN }
                or UnaryOperationNode or TypeConversionNode or ConditionalValueNode
                or SegmentedPointerNode or AbsolutePointerNode or MethodCallValueNode
                or ExplicitCastNode =>
                (ValueNode)IrAstChildren.MapChildren(node,
                    child => child is ValueNode value
                        ? JoinedValue(value, statement, alias) ?? value
                        : child),
            _ => node
        };
        return JoinedLoad(rebuilt, statement, alias) ?? rebuilt;
    }

    /// <summary><paramref name="node"/> as <c>(uint)low | ((uint)high &lt;&lt; 16)</c> (either
    /// operand order) over two word reads of one proven four-byte range: the widened pointer, or
    /// null.</summary>
    private static SegmentedPointerNode? JoinedLoad(ValueNode node, IrStatement statement,
            IrMemoryAliasAnalysis alias) {
        if (node is not BinaryOperationNode { DataType: { } orType,
                BinaryOperation: BinaryOperation.BITWISE_OR, Left: { } left, Right: { } right }
            || orType != DataType.UINT32) {
            return null;
        }
        foreach ((ValueNode maybeLow, ValueNode maybeHigh) in
            new[] { (left, right), (right, left) }) {
            if (ZeroExtendedWord(maybeLow) is not { } low
                || maybeHigh is not BinaryOperationNode { DataType: { } shiftType,
                    BinaryOperation: BinaryOperation.LEFT_SHIFT,
                    Left: { } shiftLeft, Right: { } shiftCount }
                || shiftType != DataType.UINT32
                || ZeroExtendedWord(shiftLeft) is not { } high
                || ConstantEvaluation.Resolved(shiftCount) is not { } count
                || count.Value != 16) {
                continue;
            }
            if (ProvenPair(low, high, statement, alias)) {
                return low with { DataType = DataType.UINT32 };
            }
        }
        return null;
    }

    /// <summary><paramref name="node"/> as a zero-extending <c>(uint)wordPointer</c>: the pointer,
    /// or null.</summary>
    private static SegmentedPointerNode? ZeroExtendedWord(ValueNode node) =>
        IrDoublePrecision.ZeroExtendedHalf(node, DataType.UINT16, DataType.UINT32)
            is SegmentedPointerNode { DataType: { } type } pointer
            && type == DataType.UINT16
            ? pointer
            : null;

    // ------------------------------------------------------------------
    // Store form
    // ------------------------------------------------------------------

    /// <summary><paramref name="statement"/> as a word store <c>pointer = value</c>, or not.</summary>
    private static bool IsWordStore(IrStatement statement) =>
        statement.Node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
            Left: SegmentedPointerNode { DataType: { } width }, Right: { } right }
        && width == DataType.UINT16 && right.DataType == DataType.UINT16;

    /// <summary>When the word stores at <paramref name="first"/> and <paramref name="second"/> of
    /// <paramref name="block"/> form a proven pair, <paramref name="second"/> becomes the wide
    /// store and the caller removes <paramref name="first"/>; the first value is re-evaluated at
    /// the second position, so every register and token it or the merged address reads must hold
    /// the same version at both positions.</summary>
    private static bool JoinStore(IrBlock block, int first, int second,
            IrMemoryAliasAnalysis alias, IrSsa ssa) {
        IrStatement firstStatement = block.Statements[first];
        IrStatement secondStatement = block.Statements[second];
        BinaryOperationNode firstAssign = (BinaryOperationNode)firstStatement.Node;
        BinaryOperationNode secondAssign = (BinaryOperationNode)secondStatement.Node;
        SegmentedPointerNode low = (SegmentedPointerNode)firstAssign.Left;
        SegmentedPointerNode high = (SegmentedPointerNode)secondAssign.Left;
        ValueNode lowValue = firstAssign.Right;
        ValueNode highValue = secondAssign.Right;
        // Both stored values are exact pure words; the first may not read memory at all: a read
        // could alias the second target and observe the merged order instead of the original.
        if (!IrVerifier.IsPureArm(lowValue) || !IrVerifier.IsPureArm(highValue)
            || !PlainStatement(firstStatement) || !PlainStatement(secondStatement)
            || !ProvenPair(low, high, secondStatement, alias)) {
            return false;
        }
        IrEffects moved = IrEffectsVisitor.Of(lowValue, false);
        CpuValues reads = moved.Reads;
        foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(low.Segment)
                .Concat(IrAstChildren.Descendants(high.Segment))) {
            if (descendant is SegmentRegisterNode segment) {
                reads |= IrValueSets.Of(segment);
            }
        }
        if (!ssa.SameVersions(reads, moved.TokenReads, (block, first), (block, second))) {
            return false;
        }
        SegmentedPointerNode wide = low with { DataType = DataType.UINT32 };
        // Each half keeps an explicit word truncation so a byte or word arithmetic value's
        // overflow happens before the wide join, not inside it.
        ValueNode joined = IrDoublePrecision.Pair(DataType.UINT32, lowValue, highValue, 16);
        joined = (ValueNode)IrConstantFolder.FoldConstantTerms(
            IrConstantFolder.Fold(joined, IrConstantFolder.NoVariableConstants));
        IrStatement wideStore = IrStatement.Create(
            new BinaryOperationNode(DataType.UINT32, wide, BinaryOperation.ASSIGN, joined),
            secondStatement.Origin);
        block.Statements[second] = wideStore;
        if (ssa.HasSite(secondStatement)) {
            ssa.Replace(secondStatement, wideStore);
        }
        if (ssa.HasSite(firstStatement)) {
            ssa.Remove(firstStatement);
        }
        return true;
    }
}
