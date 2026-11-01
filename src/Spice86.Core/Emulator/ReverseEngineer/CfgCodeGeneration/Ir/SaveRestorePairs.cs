namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// A <c>push X</c> ... <c>pop Y</c> pair prints without the stack - <c>savedY_SSSS = X;</c> for the
/// push, <c>Y = savedY_SSSS;</c> for the pop, or the direct <c>Y = X;</c> when nothing sits
/// between them - only when nothing can observe the removed slot: the pop's stack read is
/// provably the push's slot alone, every path between the two stays inside one region holding
/// no call, interrupt delivery, fault, stack use or read of the slot, and a later push at the
/// same SP overwrites the abandoned word before anything can read it. The pop's target is a
/// general register, a segment register other than SS, or the flag register; the push's value
/// is any expression. Printing-only, like the register locals: the IR keeps the push and the
/// pop.
/// </summary>
internal static class SaveRestorePairs {
    private static readonly CpuValues StackValues = CpuValues.Esp | CpuValues.Ss;


    /// <summary>One qualifying pair: the statement nodes to rewrite and the shared local.</summary>
    /// <param name="Push">The <see cref="StackCallNode"/> of the push statement.</param>
    /// <param name="Pop">The <c>ASSIGN</c> of the pop statement.</param>
    /// <param name="Direct">Nothing sits between the push and the pop: the pop prints
    /// <c>Y = X;</c> directly and no save local is used.</param>
    /// <param name="Pushed">The expression the push statement pushes.</param>
    /// <param name="Variable">The method-scope save local, printed on both sides.</param>
    /// <param name="Declaration">Its <c>type name = 0;</c> declaration for the method top.</param>
    /// <param name="StackAdvance">The <c>SP += width</c> statement a pop into a segment
    /// register frees its slot with: removed together with the push.</param>
    public sealed record Pair(StackCallNode Push, BinaryOperationNode Pop, bool Direct,
        ValueNode Pushed, MethodScopedVariableReferenceNode Variable, LocalDeclarationNode Declaration,
        IVisitableAstNode? StackAdvance);

    /// <summary>The pairs of one method, with the declarations to print at its top.</summary>
    /// <param name="Pairs">The qualifying pairs.</param>
    public sealed record SavedPairPlan(IReadOnlyList<Pair> Pairs) {
        /// <summary>No pair.</summary>
        public static readonly SavedPairPlan Empty = new([]);
    }

    /// <summary>Finds the qualifying pairs of <paramref name="method"/>, over the SSA and the stack
    /// slot analysis of its final IR.</summary>
    public static SavedPairPlan Plan(IrMethod method, IrSsa ssa) {
        if (!method.Blocks.SelectMany(block => block.Statements).Any(statement =>
                statement.Node is StackCallNode { Operation: StackOperation.Push })) {
            return SavedPairPlan.Empty;
        }
        IrMemoryAliasAnalysis alias = new(ssa);
        IrStackSlots slots = IrStackSlots.Compute(method, alias);
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DominatorTree<IrBlock> dominators = DominatorTreeBuilder.BuildFromEntries(graph);
        List<IrBlock> exits = [.. graph.Blocks.Where(block => graph.GetSuccessors(block).Count == 0)];
        DominatorTree<IrBlock>? postDominators = null;
        if (exits.Count > 0) {
            postDominators = DominatorTreeBuilder.BuildFromEntries(graph.Blocks, exits,
                graph.PredecessorsByBlock, graph.StableOrder);
        }
        HashSet<string> reserved = ReservedNames(method);
        List<Pair> pairs = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                if (PairOf(method, graph, dominators, postDominators, alias, slots, block, index,
                        reserved) is { } pair) {
                    pairs.Add(pair);
                }
            }
        }
        return pairs.Count == 0 ? SavedPairPlan.Empty : new SavedPairPlan(pairs);
    }

    /// <summary>Rewrites every pair of <paramref name="plan"/> inside the structured
    /// <paramref name="body"/>: a boundary holding the push or the pop gets the assignment form -
    /// or loses the push entirely for an adjacent pair. Both copies of a duplicated boundary
    /// rewrite to the same local.</summary>
    public static BlockNode Apply(BlockNode body, SavedPairPlan plan) {
        Dictionary<IVisitableAstNode, IVisitableAstNode> replacements =
            new(ReferenceEqualityComparer.Instance);
        HashSet<IVisitableAstNode> removed = new(ReferenceEqualityComparer.Instance);
        foreach (Pair pair in plan.Pairs) {
            if (pair.Direct) {
                removed.Add(pair.Push);
                replacements[pair.Pop] = pair.Pop with { Right = pair.Pushed };
            } else {
                replacements[pair.Push] = new BinaryOperationNode(pair.Variable.DataType,
                    pair.Variable, BinaryOperation.ASSIGN, pair.Pushed);
                replacements[pair.Pop] = pair.Pop with { Right = pair.Variable };
            }
            if (pair.StackAdvance is { } advance) {
                removed.Add(advance);
            }
        }
        BlockNode rewritten = (BlockNode)Rewrite(body);
        if (removed.Count != 0) {
            throw new InvalidOperationException(
                "A save/restore pair statement to remove was not a direct child of a block.");
        }
        return rewritten;

        IVisitableAstNode Rewrite(IVisitableAstNode node) {
            if (node is BlockNode block) {
                List<IVisitableAstNode> statements = [];
                foreach (IVisitableAstNode statement in block.Statements
                        .Where(statement => !removed.Remove(statement))) {
                    statements.Add(Rewrite(statement));
                }
                return new BlockNode([.. statements]);
            }
            if (replacements.TryGetValue(node, out IVisitableAstNode? replacement)) {
                return replacement;
            }
            return IrAstChildren.MapChildren(node, Rewrite);
        }
    }

    /// <summary>The declarations of <paramref name="plan"/>'s save locals, printed at the method
    /// top so the restore is always in scope and definitely assigned. Adjacent pairs print the
    /// pushed expression directly and declare nothing.</summary>
    public static IEnumerable<LocalDeclarationNode> Declarations(SavedPairPlan plan) =>
        plan.Pairs.Where(pair => !pair.Direct).Select(pair => pair.Declaration);

    /// <summary>The candidate pair of the pop at <paramref name="popIndex"/> of
    /// <paramref name="popBlock"/>, or <c>null</c> when any condition fails. The pop's target is
    /// a general register, a segment register other than SS or the flag register.</summary>
    private static Pair? PairOf(IrMethod method, BlockGraph<IrBlock> graph,
        DominatorTree<IrBlock> dominators, DominatorTree<IrBlock>? postDominators,
        IrMemoryAliasAnalysis alias, IrStackSlots slots, IrBlock popBlock, int popIndex,
        HashSet<string> reserved) {
        IrStatement popStatement = popBlock.Statements[popIndex];
        if (popStatement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN } pop) {
            return null;
        }
        IVisitableAstNode? stackAdvance = null;
        BitWidth popWidth;
        switch (pop.Left) {
            case RegisterNode { RegisterIndex: <= (int)RegisterIndex.DiIndex } popRegister
                when popRegister is not SegmentRegisterNode
                    && pop.Right is StackCallValueNode { Operation: StackOperation.Pop } popCall
                    && popCall.Width is (BitWidth.WORD_16 or BitWidth.DWORD_32)
                    && popRegister.DataType.BitWidth == popCall.Width:
                popWidth = popCall.Width;
                break;
            case FlagRegisterNode flags
                when pop.Right is StackCallValueNode { Operation: StackOperation.Pop } popCall
                    && flags.DataType.BitWidth == popCall.Width:
                popWidth = popCall.Width;
                break;
            case SegmentRegisterNode segment
                when segment.RegisterIndex != (int)SegmentRegisterIndex.SsIndex
                    && pop.Right is StackCallValueNode { Operation: StackOperation.Pop } segmentPopCall
                    && segment.DataType.BitWidth == segmentPopCall.Width:
                popWidth = segmentPopCall.Width;
                break;
            case SegmentRegisterNode segment
                when segment.RegisterIndex != (int)SegmentRegisterIndex.SsIndex:
                // A `pop ss` blocks interrupt delivery until the next instruction retires, which
                // an assignment does not reproduce; every other segment target stays in the pair.
                // When the pop is not a `Stack.Pop` call - a mov reading the top word - the SP
                // advance must be a separate statement, also part of the pair; without it the
                // push's slot is not freed and cannot be removed.
                popWidth = segment.DataType.BitWidth;
                if (popIndex + 1 >= popBlock.Statements.Count
                    || !IrStackSlots.TryStackPointerAdjustment(
                            popBlock.Statements[popIndex + 1].Node, out int adjustment)
                    || adjustment != (int)popWidth / 8) {
                    return null;
                }
                stackAdvance = popBlock.Statements[popIndex + 1].Node;
                break;
            default:
                return null;
        }
        if (postDominators is null || slots.OffsetBefore(popStatement) is not {
                Kind: IrStackOffsetKind.Known } popOffset) {
            return null;
        }
        // S1: the pop's stack read is fully attributed to exactly one slot definition - the
        // top word, whose statement is a push of the same width.
        StackCallNode? push = null;
        IrStatement? pushStatement = null;
        ValueNode? pushed = null;
        foreach (IrStackSlotRead read in slots.ReadsOf(popStatement)) {
            if (!read.Complete || read.Delta != popOffset.Delta || read.Definitions.Count != 1
                || read.Definitions.Single().Statement is not { } definition
                || definition.Node is not StackCallNode {
                    Operation: StackOperation.Push } candidate
                || candidate.Width != popWidth
                || candidate.Arguments is not [ValueNode pushedValue]
                || pushedValue.DataType.BitWidth != pop.Left.DataType.BitWidth) {
                continue;
            }
            push = candidate;
            pushStatement = definition;
            pushed = pushedValue;
        }
        if (push is null || pushStatement is null || pushed is null) {
            return null;
        }
        (IrBlock pushBlock, int pushIndex) = SiteOf(method, pushStatement);
        // S2: the push dominates the pop and the pop post-dominates the push, so every path
        // between them stays inside the region; an exit, throw or jump out would break one of the two.
        if (!StrictlyDominates(dominators, true, pushBlock, pushIndex, popBlock, popIndex)
            || !StrictlyDominates(postDominators, false, popBlock, popIndex, pushBlock, pushIndex)) {
            return null;
        }
        int width = (int)popWidth / 8;
        foreach ((IrBlock betweenBlock, int betweenIndex) in
                Between(dominators, pushBlock, pushIndex, popBlock, popIndex)) {
            IrStatement between = betweenBlock.Statements[betweenIndex];
            // S3: no call, int, callback, event check or other interrupt delivery, and no fault.
            if (between.Effects.MayFault || between.Effects.DeliversInterrupts
                || alias.IsFullMemoryBoundary(betweenBlock, betweenIndex)) {
                return null;
            }
            // S4: nothing touches SP/ESP/SS and there is no stack operation at all.
            if (((between.Effects.Reads | between.Effects.MayWrite) & StackValues) != CpuValues.None
                || IrAstChildren.Descendants(between.Node)
                    .Any(node => node is StackCallNode or StackCallValueNode)) {
                return null;
            }
            // S5: no memory read can see the pushed word.
            IrMemoryAccess slot = alias.StackAccess(betweenBlock, betweenIndex, 0, width);
            if (alias.Reads(betweenBlock, betweenIndex).Any(read =>
                    alias.Relation(read, slot) != IrAliasRelation.Disjoint)) {
                return null;
            }
        }
        // The terminator of a block inside the region runs between the push and the pop too:
        // a branch condition or a switch value can read memory (a flag forward moves it there),
        // so the same conditions apply to it. A block the pop dominates is not part of the
        // region - its terminator runs after the pop and is covered after.
        foreach (IrBlock betweenBlock in dominators.GetDominatedBlocks(pushBlock)) {
            if (dominators.Dominates(popBlock, betweenBlock)) {
                continue;
            }
            IrTerminator terminator = betweenBlock.Terminator;
            // S3
            if (terminator.Effects.MayFault || terminator.Effects.DeliversInterrupts
                || (terminator.Effects.TokenWrites & IrTokens.Memory) != IrTokens.None) {
                return null;
            }
            // S4
            if (((terminator.Effects.Reads | terminator.Effects.MayWrite) & StackValues)
                    != CpuValues.None
                || IrAstChildren.TerminatorNodes(terminator)
                    .Any(node => node is StackCallNode or StackCallValueNode)) {
                return null;
            }
            // S5
            IrMemoryAccess slot = alias.StackAccess(betweenBlock,
                betweenBlock.Statements.Count, 0, width);
            if (alias.Reads(betweenBlock, terminator).Any(read =>
                    alias.Relation(read, slot) != IrAliasRelation.Disjoint)) {
                return null;
            }
        }

        // S6: on every path after the pop, a push overwrites the abandoned word before anything
        // can observe it.
        if (!CoveredAfter(graph, alias, slots, popBlock, popIndex, width,
                popOffset.Base, popOffset.Delta)) {
            return null;
        }
        string stem = $"saved{TargetName(pop.Left)}_{pushStatement.Origin.Address.Offset:X4}";
        string name = stem;
        for (int suffix = 2; !reserved.Add(name); suffix++) {
            name = $"{stem}_{suffix}";
        }
        // Adjacency for the direct form: inside one block, every statement between the push and
        // the pop only prints a comment line (the pop's own instruction-start comment). Anything
        // else, or a block boundary - a printed transfer or label - asks for the save local.
        bool direct = ReferenceEquals(pushBlock, popBlock)
            && Enumerable.Range(pushIndex + 1, popIndex - pushIndex - 1).All(index =>
                popBlock.Statements[index].Node is InstructionStartNode or CommentNode);
        DataType type = DataType.UnsignedFromBitWidth(popWidth);
        MethodScopedVariableReferenceNode variable = new(type, name);
        return new Pair(push, pop, direct, pushed, variable,
            new LocalDeclarationNode(type, name, new ConstantNode(type, 0)), stackAdvance);
    }

    /// <summary>The block and statement index of <paramref name="statement"/> in
    /// <paramref name="method"/>.</summary>
    private static (IrBlock, int) SiteOf(IrMethod method, IrStatement statement) {
        foreach (IrBlock block in method.Blocks) {
            int index = block.Statements.IndexOf(statement);
            if (index >= 0) {
                return (block, index);
            }
        }
        throw new InvalidOperationException("The push statement is not in its method.");
    }

    /// <summary>Whether the statement at <paramref name="firstIndex"/> of
    /// <paramref name="firstBlock"/> strictly dominates the one at <paramref name="secondIndex"/> of
    /// <paramref name="secondBlock"/> on <paramref name="tree"/>. With <paramref name="forward"/>
    /// false the tree is the post-dominator one: inside one block the later statement
    /// leads there.</summary>
    private static bool StrictlyDominates(DominatorTree<IrBlock> tree, bool forward,
        IrBlock firstBlock, int firstIndex, IrBlock secondBlock, int secondIndex) {
        if (ReferenceEquals(firstBlock, secondBlock)) {
            return forward ? firstIndex < secondIndex : firstIndex > secondIndex;
        }
        return tree.Dominates(firstBlock, secondBlock);
    }

    /// <summary>The statement positions strictly between the push and the pop: every block the
    /// push dominates, except the statements at or after the pop.</summary>
    private static IEnumerable<(IrBlock, int)> Between(DominatorTree<IrBlock> dominators,
        IrBlock pushBlock, int pushIndex, IrBlock popBlock, int popIndex) {
        foreach (IrBlock block in dominators.GetDominatedBlocks(pushBlock)) {
            int start = ReferenceEquals(block, pushBlock) ? pushIndex + 1 : 0;
            int end = block.Statements.Count;
            if (ReferenceEquals(block, popBlock)) {
                end = popIndex;
            } else if (dominators.Dominates(popBlock, block)) {
                end = 0;
            }
            for (int index = start; index < end; index++) {
                yield return (block, index);
            }
        }
    }

    /// <summary>Whether every path after the pop at <paramref name="popIndex"/> reaches a push of
    /// the same width overwriting the abandoned slot before a read of it, a call, a fault, an
    /// interrupt delivery or a way out of the method. The slot sits at <paramref name="slotDelta"/>
    /// from the base <paramref name="slotBase"/> kept by the slot analysis; a plain
    /// <c>SP += c</c> adjustment observes nothing and only moves SP, so it is skipped over.
    /// A path that loops back to a statement already seen either covered the slot or never
    /// observes it.</summary>
    private static bool CoveredAfter(BlockGraph<IrBlock> graph, IrMemoryAliasAnalysis alias,
        IrStackSlots slots, IrBlock popBlock, int popIndex, int width,
        IrStatement? slotBase, int slotDelta) {
        Stack<(IrBlock, int)> pending = [];
        pending.Push((popBlock, popIndex + 1));
        HashSet<(IrBlock, int)> visited = [];
        while (pending.Count > 0) {
            (IrBlock block, int index) = pending.Pop();
            if (index >= block.Statements.Count) {
                // The terminator runs before the successors: a branch condition or a switch
                // value can observe the abandoned word the same way a statement read can.
                if (slots.OffsetAtBlockEnd(block) is not { Kind: IrStackOffsetKind.Known }
                        endOffset || !ReferenceEquals(endOffset.Base, slotBase)) {
                    return false;
                }
                IrTerminator terminator = block.Terminator;
                if (terminator.Effects.MayFault || terminator.Effects.DeliversInterrupts
                    || (terminator.Effects.TokenWrites & IrTokens.Memory) != IrTokens.None
                    || (terminator.Effects.MayWrite & StackValues) != CpuValues.None
                    || IrAstChildren.TerminatorNodes(terminator)
                        .Any(node => node is StackCallNode or StackCallValueNode)) {
                    return false;
                }
                IrMemoryAccess endSlot = alias.StackAccess(block, index,
                    slotDelta - endOffset.Delta, width);
                if (alias.Reads(block, terminator).Any(read =>
                        alias.Relation(read, endSlot) != IrAliasRelation.Disjoint)) {
                    return false;
                }
                IReadOnlyList<IrBlock> successors = graph.GetSuccessors(block);
                if (successors.Count == 0) {
                    return false;
                }
                foreach (IrBlock successor in successors) {
                    pending.Push((successor, 0));
                }
                continue;
            }
            if (!visited.Add((block, index))) {
                continue;
            }
            IrStatement statement = block.Statements[index];
            if (slots.OffsetBefore(statement) is not { Kind: IrStackOffsetKind.Known } offset
                || !ReferenceEquals(offset.Base, slotBase)) {
                return false;
            }
            IrMemoryAccess slot = alias.StackAccess(block, index, slotDelta - offset.Delta, width);
            if (statement.Node is StackCallNode { Operation: StackOperation.Push } push) {
                IReadOnlyList<IrMemoryAccess> writes = alias.Writes(block, index);
                if (push.Width == (BitWidth)(width * 8) && writes.Count == 1
                    && alias.Relation(writes[0], slot) == IrAliasRelation.Equal) {
                    continue;
                }
                return false;
            }
            if (IrStackSlots.TryStackPointerAdjustment(statement.Node, out int _)) {
                pending.Push((block, index + 1));
                continue;
            }
            if (IrAstChildren.Descendants(statement.Node)
                    .Any(node => node is StackCallNode or StackCallValueNode)
                || (statement.Effects.MayWrite & StackValues) != CpuValues.None) {
                return false;
            }
            if (statement.Effects.MayFault || statement.Effects.DeliversInterrupts
                || alias.IsFullMemoryBoundary(block, index)) {
                return false;
            }
            if (alias.Reads(block, index).Any(read =>
                    alias.Relation(read, slot) != IrAliasRelation.Disjoint)) {
                return false;
            }
            pending.Push((block, index + 1));
        }
        return true;
    }

    /// <summary>Every variable name already taken in <paramref name="method"/>, so a save local
    /// never collides.</summary>
    private static HashSet<string> ReservedNames(IrMethod method) {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (VariableDeclarationNode variable in method.MethodVariables) {
            names.Add(variable.VariableName);
        }
        foreach (IrBlock block in method.Blocks) {
            foreach (IVisitableAstNode node in block.Statements
                    .SelectMany(statement => IrAstChildren.Descendants(statement.Node))
                    .Where(node => node is VariableReferenceNode or VariableDeclarationNode
                        or LocalDeclarationNode)) {
                if (node is VariableReferenceNode reference) {
                    names.Add(reference.VariableName);
                } else if (node is VariableDeclarationNode declaration) {
                    names.Add(declaration.VariableName);
                } else {
                    names.Add(((LocalDeclarationNode)node).VariableName);
                }
            }
        }
        return names;
    }

    /// <summary>The save-local name stem of the pop's target: the register or segment register
    /// name, <c>Flags</c> for the flag register.</summary>
    private static string TargetName(ValueNode target) => target switch {
        SegmentRegisterNode segment => RegisterNames.PascalOf(RegisterNames.Segments[segment.RegisterIndex]),
        RegisterNode register => RegisterName(register),
        FlagRegisterNode => "Flags",
        _ => "Value"
    };

    private static string RegisterName(RegisterNode register) {
        string name = register.DataType.BitWidth == BitWidth.WORD_16 ? RegisterNames.Word16[register.RegisterIndex] : $"e{RegisterNames.Word16[register.RegisterIndex]}";
        return string.Concat(name[..1].ToUpperInvariant(), name.AsSpan(1));
    }
}
