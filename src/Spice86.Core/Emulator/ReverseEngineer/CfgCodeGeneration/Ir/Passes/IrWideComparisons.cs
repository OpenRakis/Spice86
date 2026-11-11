namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Late condition pass: folds the lowered multi-test decision trees of a 32-bit comparison into
/// one branch and inlines the final compare setup of an or-test or a range test.
/// <para>Equality chains - <c>high != other ? T : (low != lowOther ? T : F)</c> and the
/// <c>==</c> mirror, in either branch-target orientation - print
/// <c>packed != packed</c> / <c>packed == packed</c>; the signed or unsigned three-way
/// <c>high &lt; ? T : (high &gt; ? F : (low &lt;u ? T : F))</c> prints
/// <c>(int)packed &lt; (int)packed</c> (or the unsigned form, <c>&lt;=</c>, <c>&gt;</c>,
/// <c>&gt;=</c> by the last compare). Every inner test block must carry only markers, be entered
/// solely from the matched outer tests through real instruction edges, and never be a method
/// entry or a structural-successor anchor; every compared operand must read the same versions at
/// the root terminator as at the inner terminator that used to read it.</para>
/// <para>Independently, a block whose last effective statement is <c>r = a | b</c> tested for
/// zero, or <c>r = x - lower</c> compared unsigned to a constant span, and whose defined versions
/// feed only that branch, substitutes the right side into the condition so the constant folder
/// prints the or-test or the range, then removes the dead assignment.</para>
/// </summary>
internal static class IrWideComparisons {
    /// <summary>Folds the wide comparisons of <paramref name="method"/>'s branches; returns the
    /// number of rewritten roots. The caller rebuilds the SSA and webs when it is not zero.</summary>
    public static int Apply(IrMethod method, IrSsa ssa) {
        Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors =
            method.PredecessorEdges();
        HashSet<ICfgNode> pinned = IrInstructionIdioms.PinnedOriginsOf(method);
        HashSet<IrBlock> entryBlocks = [.. method.Entries.Select(entry => entry.Block)];
        HashSet<IrBlock> claimed = [];
        List<(IrBlock Block, IrBranch Terminator)> branchEdits = [];
        List<(IrBlock Block, int Index, IrStatement Replacement)> replacedStatements = [];
        List<(IrBlock Block, int Index)> removedStatements = [];
        List<IrBlock> removedBlocks = [];
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrBranch branch || claimed.Contains(block)) {
                continue;
            }
            List<IrBlock> inner = [];
            if ((EqualityCondition(block, branch, predecessors, pinned, entryBlocks, inner, ssa)
                ?? ThreeWayCondition(block, branch, predecessors, pinned, entryBlocks, inner,
                    ssa)
                ?? AndOrCondition(block, branch, predecessors, pinned, entryBlocks, inner,
                    ssa)) is { } wide) {
                if (inner.Any(claimed.Contains)) {
                    continue;
                }
                // The emitted edges keep the last test's origin: its CFG successors are exactly
                // the two leaves, while the root test's own successor was the removed chain.
                branchEdits.Add((block, new IrBranch(wide.Origin, wide.Condition,
                    new IrEdge(wide.WhenTrue, false), new IrEdge(wide.WhenFalse, false))));
                claimed.Add(block);
                claimed.UnionWith(inner);
                removedBlocks.AddRange(inner);
                continue;
            }
            if (InlinedCondition(method, block, branch, ssa, pinned) is { } inlined) {
                branchEdits.Add((block, new IrBranch(branch.Origin, inlined.Condition,
                    branch.WhenTrue, branch.WhenFalse)));
                if (inlined.Replacement is { } replacement) {
                    replacedStatements.Add((block, inlined.Index, replacement));
                } else {
                    removedStatements.Add((block, inlined.Index));
                }
                claimed.Add(block);
            }
        }
        foreach ((IrBlock block, IrBranch terminator) in branchEdits) {
            if (ssa.HasSite(block.Terminator)) {
                ssa.ReplaceTerminator(block.Terminator, terminator);
            }
            block.Terminator = terminator;
        }
        foreach ((IrBlock block, int index, IrStatement replacement) in replacedStatements) {
            IrStatement old = block.Statements[index];
            block.Statements[index] = replacement;
            if (ssa.HasSite(old)) {
                ssa.Replace(old, replacement);
            }
        }
        foreach ((IrBlock block, int index) in removedStatements) {
            IrStatement old = block.Statements[index];
            block.Statements.RemoveAt(index);
            if (ssa.HasSite(old)) {
                ssa.Remove(old);
            }
        }
        method.Blocks.RemoveAll(removedBlocks.Contains);
        return branchEdits.Count;
    }

    // ------------------------------------------------------------------
    // Comparison shape
    // ------------------------------------------------------------------

    /// <summary>The comparison <paramref name="condition"/> computes after folding a unary NOT
    /// into the operator, or null for a non-relational condition.</summary>
    private static (BinaryOperation Operation, ValueNode Left, ValueNode Right)? CompareOf(
            ValueNode condition) {
        if (condition is UnaryOperationNode {
                UnaryOperation: UnaryOperation.NOT, Value: ValueNode inner }) {
            return CompareOf(inner) is { } compare
                ? (Invert(compare.Operation), compare.Left, compare.Right)
                : null;
        }
        return condition is BinaryOperationNode {
                BinaryOperation: var op, Left: var left, Right: var right } && IsRelation(op)
            ? (op, left, right)
            : null;
    }

    private static bool IsRelation(BinaryOperation operation) => operation
        is BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL or BinaryOperation.LESS_THAN
        or BinaryOperation.GREATER_THAN or BinaryOperation.LESS_THAN_OR_EQUAL
        or BinaryOperation.GREATER_THAN_OR_EQUAL;

    private static BinaryOperation Invert(BinaryOperation operation) => operation switch {
        BinaryOperation.EQUAL => BinaryOperation.NOT_EQUAL,
        BinaryOperation.NOT_EQUAL => BinaryOperation.EQUAL,
        BinaryOperation.LESS_THAN => BinaryOperation.GREATER_THAN_OR_EQUAL,
        BinaryOperation.GREATER_THAN_OR_EQUAL => BinaryOperation.LESS_THAN,
        BinaryOperation.GREATER_THAN => BinaryOperation.LESS_THAN_OR_EQUAL,
        BinaryOperation.LESS_THAN_OR_EQUAL => BinaryOperation.GREATER_THAN,
        _ => operation
    };

    /// <summary>The targets of <paramref name="branch"/> read as
    /// <c>left expectedOp right ? whenTrue : whenFalse</c>: when the condition is the inverted
    /// relation the edges swap. Null for another condition.</summary>
    private static (ValueNode Left, ValueNode Right, IrBlock WhenTrue, IrBlock WhenFalse)?
            Step(IrBranch branch, BinaryOperation expected) {
        if (CompareOf(branch.Condition) is not { } compare) {
            return null;
        }
        if (compare.Operation == expected) {
            return (compare.Left, compare.Right, branch.WhenTrue.Target,
                branch.WhenFalse.Target);
        }
        if (compare.Operation == Invert(expected)) {
            return (compare.Left, compare.Right, branch.WhenFalse.Target,
                branch.WhenTrue.Target);
        }
        // The algebra turns an unsigned `x < 1`/`x <= 0` into `x == 0` and `x >= 1`/`x > 0`
        // into `x != 0`; only an unsigned word can serve as the expected relation that way -
        // a signed `x == 0` is not a signed `x < 1`.
        if (compare.Operation is BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL
            && ZeroTestSide(compare) is { } word
            && word.DataType.BitWidth == BitWidth.WORD_16
            && !word.DataType.Signed) {
            (BinaryOperation Operation, int Constant)[] versions =
                compare.Operation == BinaryOperation.EQUAL
                    ? [(BinaryOperation.LESS_THAN, 1), (BinaryOperation.LESS_THAN_OR_EQUAL, 0)]
                    : [(BinaryOperation.GREATER_THAN_OR_EQUAL, 1),
                        (BinaryOperation.GREATER_THAN, 0)];
            foreach ((BinaryOperation operation, int constant) in versions) {
                ConstantNode bound = new(DataType.UINT16, (ulong)constant);
                if (operation == expected) {
                    return (word, bound, branch.WhenTrue.Target, branch.WhenFalse.Target);
                }
                if (operation == Invert(expected)) {
                    return (word, bound, branch.WhenFalse.Target, branch.WhenTrue.Target);
                }
            }
        }
        return null;
    }

    /// <summary>The non-zero side of an <c>x == 0</c>/<c>x != 0</c> test, or null.</summary>
    private static ValueNode? ZeroTestSide(
            (BinaryOperation Operation, ValueNode Left, ValueNode Right) compare) =>
        ConstantEvaluation.Resolved(compare.Right) is { Value: 0 } ? compare.Left
            : ConstantEvaluation.Resolved(compare.Left) is { Value: 0 }
                ? compare.Right : null;

    /// <summary>Strips same-width conversions (signedness only) around
    /// <paramref name="node"/>.</summary>
    private static ValueNode SameWidth(ValueNode node) =>
        node is TypeConversionNode conversion
            && conversion.DataType.BitWidth == conversion.Value.DataType.BitWidth
            ? SameWidth(conversion.Value)
            : node;

    /// <summary>Two comparison operands are the same word value modulo a signedness cast.</summary>
    private static bool SameOperand(ValueNode first, ValueNode second) =>
        SameWidth(first).Equals(SameWidth(second));

    /// <summary><paramref name="node"/> is a side-effect-free 16-bit value.</summary>
    private static bool IsWord(ValueNode node) =>
        SameWidth(node).DataType.BitWidth == BitWidth.WORD_16 && IrVerifier.IsPureArm(node);

    /// <summary>Whether <paramref name="inner"/> is a removable test block: only markers, a
    /// branch terminator, no entry, no pinned origin, entered only from
    /// <paramref name="predecessor"/> through real instruction edges.</summary>
    private static bool InnerBlock(IrBlock inner, IrBlock predecessor,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<ICfgNode> pinned, HashSet<IrBlock> entryBlocks) {
        if (inner == predecessor || entryBlocks.Contains(inner)
            || inner.Terminator is not IrBranch
            || inner.Statements.Any(statement => !IrInstructionIdioms.IsMarker(statement)
                || pinned.Contains(statement.Origin))) {
            return false;
        }
        if (!predecessors.TryGetValue(inner, out List<(IrBlock Block, IrEdge Edge)>? incoming)
            || incoming.Any(edge => edge.Block != predecessor || edge.Edge.InsideInstruction)) {
            return false;
        }
        return inner.Terminator.Edges.All(edge => !edge.InsideInstruction);
    }

    /// <summary>The versions the condition of <paramref name="branch"/> reads at
    /// <paramref name="position"/> equal those at <paramref name="rootPosition"/>.</summary>
    private static bool SameVersions(IrSsa ssa, IrBranch branch,
            (IrBlock Block, int Index) position, (IrBlock Block, int Index) rootPosition) {
        IrEffects effects = branch.Effects;
        return ssa.SameVersions(effects.Reads, effects.TokenReads, rootPosition, position);
    }

    // ------------------------------------------------------------------
    // Equality chains
    // ------------------------------------------------------------------

    /// <summary><c>high != other ? T : (low != lowOther ? T : F)</c> packs to
    /// <c>packed != packed</c>; <c>high == other ? (low == lowOther ? T : F) : F</c> packs to
    /// <c>packed == packed</c>. The root always continues when the high halves are equal; the
    /// low test's operator and which of its leaves is the root's leave leaf prove the chain is
    /// a single wide equality, whose emitted edges are the low test's own leaves.</summary>
    private static (ICfgNode Origin, ValueNode Condition, IrBlock WhenTrue, IrBlock WhenFalse)? EqualityCondition(
            IrBlock root, IrBranch branch,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<ICfgNode> pinned, HashSet<IrBlock> entryBlocks, List<IrBlock> inner,
            IrSsa ssa) {
        if (CompareOf(branch.Condition) is not { } compare
            || compare.Operation is not (BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL)
            || !IsWord(compare.Left) || !IsWord(compare.Right)) {
            return null;
        }
        // EQUAL continues on true, NOT_EQUAL on false: the root leaves only when the high
        // halves differ.
        IrBlock continueTarget = compare.Operation == BinaryOperation.EQUAL
            ? branch.WhenTrue.Target : branch.WhenFalse.Target;
        IrBlock leaveTarget = compare.Operation == BinaryOperation.EQUAL
            ? branch.WhenFalse.Target : branch.WhenTrue.Target;
        if (!InnerBlock(continueTarget, root, predecessors, pinned, entryBlocks)
            || continueTarget.Terminator is not IrBranch lowBranch
            || CompareOf(lowBranch.Condition) is not { } low
            || low.Operation is not (BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL)
            || !IsWord(low.Left) || !IsWord(low.Right)) {
            return null;
        }
        // The high halves differ means the packed compare decided: for an == chain the packed
        // result is false, so the low test's false leaf is the root's leave leaf; for a !=
        // chain it is the true leaf. Either branch-target orientation folds the same way.
        IrBlock whenTrue = lowBranch.WhenTrue.Target;
        IrBlock whenFalse = lowBranch.WhenFalse.Target;
        if (low.Operation == BinaryOperation.EQUAL && whenFalse != leaveTarget
            || low.Operation == BinaryOperation.NOT_EQUAL && whenTrue != leaveTarget
            || whenTrue == whenFalse) {
            return null;
        }
        if (!SameVersions(ssa, lowBranch, (continueTarget, continueTarget.Statements.Count),
                (root, root.Statements.Count))) {
            return null;
        }
        inner.Add(continueTarget);
        return (lowBranch.Origin, new BinaryOperationNode(DataType.BOOL,
            Pack(low.Left, compare.Left), low.Operation, Pack(low.Right, compare.Right)),
            whenTrue, whenFalse);
    }

    // ------------------------------------------------------------------
    // Three-way comparisons
    // ------------------------------------------------------------------

    /// <summary><c>high &lt; other ? T : (high &gt; other ? F : (low &lt;u lowOther ? T : F))</c>
    /// and the <c>&gt;</c> mirror pack to one wide relation, in either encoded orientation;
    /// <c>&lt;=</c>/<c>&gt;=</c> come from the low compare's operator.</summary>
    private static (ICfgNode Origin, ValueNode Condition, IrBlock WhenTrue, IrBlock WhenFalse)? ThreeWayCondition(
            IrBlock root, IrBranch branch,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<ICfgNode> pinned, HashSet<IrBlock> entryBlocks, List<IrBlock> inner,
            IrSsa ssa) {
        // Read the root as `high dir other ? leafA : mid` for each strict direction.
        foreach (BinaryOperation direction in new[] {
                     BinaryOperation.LESS_THAN, BinaryOperation.GREATER_THAN }) {
            if (Step(branch, direction) is { } first) {
                if (TryThreeWay(root, first, direction, predecessors, pinned, entryBlocks, inner,
                        ssa) is { } wide) {
                    return wide;
                }
            }
        }
        return null;
    }

    private static (ICfgNode Origin, ValueNode Condition, IrBlock WhenTrue, IrBlock WhenFalse)? TryThreeWay(
            IrBlock root,
            (ValueNode Left, ValueNode Right, IrBlock WhenTrue, IrBlock WhenFalse) first,
            BinaryOperation direction,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<ICfgNode> pinned, HashSet<IrBlock> entryBlocks, List<IrBlock> inner,
            IrSsa ssa) {
        IrBlock leafA = first.WhenTrue;
        IrBlock mid = first.WhenFalse;
        if (!IsWord(first.Left) || !IsWord(first.Right)
            || leafA == mid
            || !InnerBlock(mid, root, predecessors, pinned, entryBlocks)) {
            return null;
        }
        if (mid.Terminator is not IrBranch midBranch
            || Step(midBranch, Opposite(direction)) is not { } second
            || !SameOperand(first.Left, second.Left)
            || !SameOperand(first.Right, second.Right)
            // The two high compares must read the same signedness: `signed high <`
            // followed by `unsigned high >` is not one three-way test. The two operands
            // of one test must share it too: packing a signed left and an unsigned right
            // under one `(int)` cast compares the right half under the wrong sign.
            || first.Left.DataType.Signed != second.Left.DataType.Signed
            || first.Right.DataType.Signed != second.Right.DataType.Signed
            || first.Left.DataType.Signed != first.Right.DataType.Signed) {
            return null;
        }
        IrBlock leafB = second.WhenTrue;
        IrBlock low = second.WhenFalse;
        if (leafB == leafA
            || !InnerBlock(low, mid, predecessors, pinned, entryBlocks)
            || low.Terminator is not IrBranch lowBranch) {
            return null;
        }
        // The low test must end at the same two leaves; its operator, read in the leafA
        // direction, must be the unsigned strict/inclusive form of the chain's direction.
        foreach (BinaryOperation lowWanted in new[] { direction, Inclusive(direction) }) {
            if (Step(lowBranch, lowWanted) is not { } third) {
                continue;
            }
            if (third.WhenTrue != leafA || third.WhenFalse != leafB
                || !UnsignedWordOperand(third.Left) || !UnsignedWordOperand(third.Right)) {
                continue;
            }
            (IrBlock, int) rootPosition = (root, root.Statements.Count);
            if (!SameVersions(ssa, midBranch, (mid, mid.Statements.Count), rootPosition)
                || !SameVersions(ssa, lowBranch, (low, low.Statements.Count), rootPosition)) {
                return null;
            }
            inner.Add(mid);
            inner.Add(low);
            ValueNode left = Pack(third.Left, first.Left);
            ValueNode right = Pack(third.Right, first.Right);
            // Signedness comes only from the high comparison: signed words sign-extend the
            // packed operands, unsigned words compare as uint.
            if (first.Left.DataType.Signed) {
                left = new TypeConversionNode(DataType.INT32, left);
                right = new TypeConversionNode(DataType.INT32, right);
            }
            return (lowBranch.Origin, new BinaryOperationNode(DataType.BOOL, left, lowWanted,
                right), leafA, leafB);
        }
        return null;
    }

    /// <summary>An unsigned word operand of the low test: an unsigned word value, or a constant
    /// that fits one. Signedness is judged before any cast stripping: `(short)AX` is not an
    /// unsigned word merely because the register inside is `UINT16`.</summary>
    private static bool UnsignedWordOperand(ValueNode node) {
        if (node is ConstantNode constant) {
            return constant.Value is >= 0 and <= 0xFFFF;
        }
        return !node.DataType.Signed && node.DataType.BitWidth == BitWidth.WORD_16
            && IrVerifier.IsPureArm(node);
    }

    /// <summary>A comparison node rebuilt, possibly with <paramref name="operation"/> inverted so
    /// a branch target swap prints as the opposite relation instead of <c>!()</c>.</summary>
    private static ValueNode CompareNode(BinaryOperation operation, ValueNode left,
            ValueNode right) =>
        new BinaryOperationNode(DataType.BOOL, left, operation, right);

    /// <summary><c>a ? inner : other</c> with <c>inner: b ? leafT : other</c> is
    /// <c>a &amp;&amp; b ? leafT : other</c>; the same tree with the leaves swapped is
    /// <c>!a || b</c>, <c>a || b</c> or <c>a &amp;&amp; b</c> by orientation. Both compares'
    /// operands evaluate at most once and in order under the logical operator, so the merged
    /// condition is exact for any pure-or-impure operands.</summary>
    private static (ICfgNode Origin, ValueNode Condition, IrBlock WhenTrue, IrBlock WhenFalse)?
            AndOrCondition(IrBlock root, IrBranch branch,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<ICfgNode> pinned, HashSet<IrBlock> entryBlocks, List<IrBlock> inner,
            IrSsa ssa) {
        if (CompareOf(branch.Condition) is not { } a) {
            return null;
        }
        foreach (bool innerOnTrue in new[] { true, false }) {
            IrBlock innerBlock = innerOnTrue ? branch.WhenTrue.Target : branch.WhenFalse.Target;
            IrBlock other = innerOnTrue ? branch.WhenFalse.Target : branch.WhenTrue.Target;
            if (innerBlock == root
                || !InnerBlock(innerBlock, root, predecessors, pinned, entryBlocks)) {
                continue;
            }
            if (innerBlock.Terminator is not IrBranch lowBranch
                || CompareOf(lowBranch.Condition) is not { } b) {
                return null;
            }
            if (!SameVersions(ssa, lowBranch, (innerBlock, innerBlock.Statements.Count),
                    (root, root.Statements.Count))) {
                return null;
            }
            if (lowBranch.WhenFalse.Target == other && lowBranch.WhenTrue.Target != other) {
                // `a ? (b ? leafT : other) : other` is `a && b`; `a ? other : (b ? leafT :
                // other)` is `!a && b`. Both decide leafT vs other. The merge is only taken
                // when the printer renders it as a relational range: a general `&&` of two
                // tests reads worse than the boolean structure the AST combiner produces.
                BinaryOperationNode candidate = new(DataType.BOOL,
                    CompareNode(innerOnTrue ? a.Operation : Invert(a.Operation), a.Left,
                        a.Right),
                    BinaryOperation.LOGICAL_AND,
                    CompareNode(b.Operation, b.Left, b.Right));
                if (!CSharpExpressionPrinter.CanPrintRange(candidate)) {
                    return null;
                }
                inner.Add(innerBlock);
                return (lowBranch.Origin, candidate, lowBranch.WhenTrue.Target, other);
            }
            if (lowBranch.WhenTrue.Target == other && lowBranch.WhenFalse.Target != other) {
                // `a ? (b ? leafT : leafF) : leafT` is `!a || b`; `a ? leafT : (b ? leafT :
                // leafF)` is `a || b`. Both decide leafT vs leafF, with the same range gate.
                BinaryOperationNode candidate = new(DataType.BOOL,
                    CompareNode(innerOnTrue ? Invert(a.Operation) : a.Operation, a.Left,
                        a.Right),
                    BinaryOperation.LOGICAL_OR,
                    CompareNode(b.Operation, b.Left, b.Right));
                if (!CSharpExpressionPrinter.CanPrintRange(candidate)) {
                    return null;
                }
                inner.Add(innerBlock);
                return (lowBranch.Origin, candidate, other, lowBranch.WhenFalse.Target);
            }
            return null;
        }
        return null;
    }

    private static BinaryOperation Opposite(BinaryOperation operation) => operation switch {
        BinaryOperation.LESS_THAN => BinaryOperation.GREATER_THAN,
        BinaryOperation.GREATER_THAN => BinaryOperation.LESS_THAN,
        _ => operation
    };

    private static BinaryOperation Inclusive(BinaryOperation operation) => operation switch {
        BinaryOperation.LESS_THAN => BinaryOperation.LESS_THAN_OR_EQUAL,
        BinaryOperation.GREATER_THAN => BinaryOperation.GREATER_THAN_OR_EQUAL,
        _ => operation
    };

    /// <summary><c>(uint)(ushort)low | ((uint)(ushort)high &lt;&lt; 16)</c>, never
    /// sign-extended; two constant halves fold to their dword literal.</summary>
    private static ValueNode Pack(ValueNode low, ValueNode high) =>
        (ValueNode)IrConstantFolder.FoldConstantTerms(IrConstantFolder.Fold(
            IrDoublePrecision.Pair(DataType.UINT32, low, high, 16),
            IrConstantFolder.NoVariableConstants));

    // ------------------------------------------------------------------
    // Branch-consumed assignments
    // ------------------------------------------------------------------

    /// <summary>The branch condition with the last assignment's right side inlined and its
    /// index, when every version the statement defines feeds only the branch and the pair is an
    /// or-test or a range test.</summary>
    private static (ValueNode Condition, int Index, IrStatement? Replacement)? InlinedCondition(
            IrMethod method, IrBlock block, IrBranch branch, IrSsa ssa,
            HashSet<ICfgNode> pinned) {
        int last = -1;
        for (int i = block.Statements.Count - 1; i >= 0; i--) {
            if (!IrInstructionIdioms.IsMarker(block.Statements[i])) {
                last = i;
                break;
            }
        }
        if (last < 0) {
            return null;
        }
        IrStatement writer = block.Statements[last];
        if (writer.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode target, Right: ValueNode right }
            || target is SegmentRegisterNode
            || writer.Effects.MayFault || writer.Effects.DeliversInterrupts
            || (writer.Effects.TokenReads & ~IrTokens.Memory) != IrTokens.None
            || writer.Effects.TokenWrites != IrTokens.None
            || pinned.Contains(writer.Origin)
            || CompareOf(branch.Condition) is not { } compare) {
            return null;
        }
        // Every byte every defined version contributes must be consumed only by the branch:
        // IsUsedOnlyAt follows the writer's bytes through phis and partial definitions, so a byte
        // half a join phi carries into a register whose other bytes survive the next overwrite
        // still inlines, while a phi result read anywhere else refuses. The branch must also
        // read the writer's own version, and no pinned origin may sit between the writer and
        // the terminator - a mid-block entry at the compare would skip the writer.
        foreach ((IrSsaValue value, int version) in ssa.Definitions(writer)) {
            CpuValues written = writer.Effects.MustWrite & IrSsaValues.MaskOf(value);
            if (written == CpuValues.None) {
                // Lowering can leave recorded definitions for flag writes it removed.
                continue;
            }
            if (ssa.VersionUsed(branch, value) != version
                || !ssa.IsUsedOnlyAt(version, written, branch)) {
                return null;
            }
        }
        if (IrInstructionIdioms.CrossesPinnedOrigin(block.Statements, last,
                block.Statements.Count - 1, pinned)
            || pinned.Contains(branch.Origin)) {
            return null;
        }
        bool orTest = compare.Operation is BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL
            && right is BinaryOperationNode { BinaryOperation: BinaryOperation.BITWISE_OR }
            && (Reads(compare.Left, target)
                && ConstantEvaluation.Resolved(compare.Right) is { Value: 0 }
                || Reads(compare.Right, target)
                && ConstantEvaluation.Resolved(compare.Left) is { Value: 0 });
        bool rangeTest = compare.Operation is BinaryOperation.LESS_THAN
                or BinaryOperation.GREATER_THAN or BinaryOperation.LESS_THAN_OR_EQUAL
                or BinaryOperation.GREATER_THAN_OR_EQUAL
            && !compare.Left.DataType.Signed && !compare.Right.DataType.Signed
            && Subtraction(right) is not null
            && (Reads(compare.Left, target)
                && ConstantEvaluation.Resolved(compare.Right) is not null
                || Reads(compare.Right, target)
                && ConstantEvaluation.Resolved(compare.Left) is not null);
        if (!orTest && !rangeTest) {
            return null;
        }
        if (IrVerifier.IsPureArm(right)) {
            return ((ValueNode)IrConstantFolder.FoldConstantTerms(
                IrExpressionPropagation.Substitute(branch.Condition, target, right)),
                last, null);
        }
        // An impure source cannot be duplicated into both range bounds: snapshot the subtrahend
        // once into a method-scope variable so the single memory read still happens at the
        // writer's origin, and let the folder expand `temp - lower > span` on the variable.
        if (!rangeTest || MemoryRangeSource(right) is not { } source) {
            return null;
        }
        string suffix = method.LocalSuffixes.TryGetValue(writer.Origin, out string? localSuffix)
            ? localSuffix
            : writer.Origin.Address.Offset.ToString("X4");
        string name = $"rangeOperand_{suffix}";
        MethodScopedVariableReferenceNode reference =
            new(source.Value.DataType, name);
        if (method.MethodVariables.All(variable => variable.VariableName != name)) {
            method.MethodVariables.Add(new VariableDeclarationNode(source.Value.DataType, name,
                new ConstantNode(source.Value.DataType, 0)));
        }
        BinaryOperationNode snapshot = new(source.Value.DataType, reference,
            BinaryOperation.ASSIGN, source.Value);
        IrStatement replacement = IrStatement.Create(snapshot, writer.Origin);
        BinaryOperationNode subtracted = new(source.Subtract.DataType, reference,
            BinaryOperation.MINUS, source.Subtract.Right);
        ValueNode inlined = source.Wrap is null
            ? subtracted
            : new TypeConversionNode(source.Wrap.DataType, subtracted);
        return ((ValueNode)IrConstantFolder.FoldConstantTerms(
            IrExpressionPropagation.Substitute(branch.Condition, target, inlined)),
            last, replacement);
    }

    /// <summary>The minuend <c>x</c> of a range subtraction <c>(T)(x - lower)</c> that reads
    /// memory and nothing else: x is read once by the writer, so it can move into a snapshot
    /// variable. Null for a pure or non-memory source.</summary>
    private static (ValueNode Value, BinaryOperationNode Subtract, TypeConversionNode? Wrap)?
            MemoryRangeSource(ValueNode right) {
        TypeConversionNode? wrap = right as TypeConversionNode;
        if (Subtraction(right) is not { } subtract
            || IrVerifier.IsPureArm(subtract.Left)
            || (IrEffectsVisitor.Of(subtract.Left, false).TokenReads
                & ~IrTokens.Memory) != IrTokens.None) {
            return null;
        }
        return (subtract.Left, subtract, wrap);
    }

    /// <summary><paramref name="right"/> is <c>x - lower</c>, possibly under a truncation:
    /// the lowered immediate is a frozen <see cref="InstructionFieldNode"/> or a plain
    /// constant, so the constant test goes through
    /// <see cref="ConstantEvaluation.Resolved"/>, which a mutable field fails.</summary>
    private static BinaryOperationNode? Subtraction(ValueNode right) =>
        (right as TypeConversionNode)?.Value is BinaryOperationNode wrapped
            ? ConstantSubtrahend(wrapped)
            : right is BinaryOperationNode plain
                ? ConstantSubtrahend(plain) : null;

    private static BinaryOperationNode? ConstantSubtrahend(BinaryOperationNode node) =>
        node.BinaryOperation == BinaryOperation.MINUS
        && ConstantEvaluation.Resolved(node.Right) is not null ? node : null;

    private static bool Reads(ValueNode node, RegisterNode register) =>
        node is RegisterNode self && self.Equals(register)
        || IrAstChildren.Descendants(node).OfType<RegisterNode>().Any(r => r.Equals(register));
}
