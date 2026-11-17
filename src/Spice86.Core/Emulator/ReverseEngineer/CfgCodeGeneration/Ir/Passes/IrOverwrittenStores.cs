namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Removes a store whose every byte a later store provably rewrites before anything can read
/// it: <c>mov [si],ax</c> followed by <c>mov [si],bx</c> keeps only the second store. The scan
/// covers the rest of the store's block and can continue across the control flow: past an
/// unconditional jump into a block this block alone leads to (OS-6), or past a branch whose
/// both arms each reach a covering store (OS-7). Legality rows, all through
/// <see cref="IrMemoryAliasAnalysis"/>:
/// OS-1: the removed access is proved ordinary RAM (<see cref="IrMemoryAliasAnalysis.AllowsRam"/>);
/// OS-2: a later store covers its whole byte range (<see cref="IrMemoryAliasAnalysis.Covers"/> - a
/// partial overlap keeps the store);
/// OS-3: every statement strictly between reads bytes <see cref="IrAliasRelation.Disjoint"/> from
/// the removed store - a read-modify-write's shared pointer counts - and none may fault, deliver
/// interrupts, be a speculative guard or be a <see cref="IrMemoryAliasAnalysis.IsFullMemoryBoundary"/>
/// (a call, an event check, a port write);
/// OS-4: the removed statement is a plain store with no token reads in its address or value and
/// no <see cref="IrEffects.MayFault"/>, so erasing it erases nothing else;
/// OS-5: only bytes the covering store rewrites are removed - a store with no later read is not
/// touched on that ground alone.
/// OS-6: the scan continues past an unconditional jump into a block whose only predecessor is
/// this block, provided the terminator itself passes the in-between check and the target is no
/// method entry and holds no mid-block entry;
/// OS-7: both arms of a branch must each reach a covering store through such a chain - the
/// branch condition is a statement between like any other, so a condition reading the stored
/// bytes keeps the store.
/// </summary>
internal static class IrOverwrittenStores {
    /// <summary>Applies the pass in strict mode.</summary>
    public static int Apply(IrMethod method, IrEditor editor) =>
        Apply(method, editor, GenerationPolicy.Strict);

    public static int Apply(IrMethod method, IrEditor editor, GenerationPolicy policy) {
        IrSsa ssa = editor.Ssa;
        IrMemoryAliasAnalysis alias = new(method, ssa, policy);
        Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors =
            method.PredecessorEdges();
        HashSet<IrBlock> entryBlocks = [.. method.Entries.Select(entry => entry.Block)];
        HashSet<ICfgNode> pinnedOrigins = IrInstructionIdioms.PinnedOriginsOf(method);
        List<(IrBlock Block, int Index)> removable = [];
        foreach (IrBlock block in method.Blocks) {
            for (int s = 0; s < block.Statements.Count; s++) {
                if (Removable(block, s, alias, predecessors, entryBlocks, pinnedOrigins)) {
                    removable.Add((block, s));
                }
            }
        }
        foreach ((IrBlock block, int index) in removable.OrderByDescending(pair => pair.Index)) {
            editor.Remove(block, index);
        }
        return removable.Count;
    }

    /// <summary>Whether the statement at <paramref name="s"/> is a plain store the forward paths
    /// fully cover under the legality rows: non-faulting, no token reads in its address or
    /// value, proved RAM, and a non-faulting covering write ahead with only disjoint reads in
    /// between.</summary>
    private static bool Removable(IrBlock block, int s, IrMemoryAliasAnalysis alias,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<IrBlock> entryBlocks, HashSet<ICfgNode> pinnedOrigins) {
        IrStatement store = block.Statements[s];
        if (store.Effects.MayFault
            || store.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: SegmentedPointerNode or AbsolutePointerNode } assign
            || AddressTokenReads(assign.Left) != IrTokens.None
            || IrEffectsVisitor.Of(assign.Right, false).TokenReads != IrTokens.None
            || alias.Writes(block, s) is not [IrMemoryAccess storeAccess]
            || !alias.AllowsRam(storeAccess)) {
            return false;
        }
        return CoveredAhead(block, s + 1, storeAccess, alias, predecessors, entryBlocks,
            pinnedOrigins, []);
    }

    /// <summary>Whether the statements from <paramref name="from"/> to the end of
    /// <paramref name="block"/>, and the paths its terminator leads to, rewrite every byte of
    /// <paramref name="removed"/> before anything reads them. Past the statements the scan
    /// continues through a terminator that passes the in-between check: an unconditional jump
    /// into a block with no other way in (OS-6), or both arms of a branch each reaching a
    /// covering store through such a chain (OS-7).</summary>
    private static bool CoveredAhead(IrBlock block, int from, IrMemoryAccess removed,
            IrMemoryAliasAnalysis alias,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors,
            HashSet<IrBlock> entryBlocks, HashSet<ICfgNode> pinnedOrigins,
            HashSet<IrBlock> visited) {
        for (int k = from; k < block.Statements.Count; k++) {
            if (!Between(block.Statements[k], block, k, removed, alias)) {
                return false;
            }
            if (alias.Writes(block, k) is [IrMemoryAccess cover]
                && alias.Covers(cover, removed)) {
                return true;
            }
        }
        if (!block.HasTerminator || !visited.Add(block)
            || !Between(block.Terminator, block, removed, alias)) {
            return false;
        }
        return block.Terminator switch {
            IrJump jump => ChainCovers(jump.Edge.Target, block),
            IrBranch branch => ChainCovers(branch.WhenTrue.Target, block)
                && ChainCovers(branch.WhenFalse.Target, block),
            _ => false
        };

        bool ChainCovers(IrBlock target, IrBlock fromBlock) =>
            predecessors.TryGetValue(target, out List<(IrBlock Block, IrEdge Edge)>? preds)
            && preds.Count == 1 && ReferenceEquals(preds[0].Block, fromBlock)
            && !entryBlocks.Contains(target)
            && !target.Statements.Skip(1).Any(statement
                => pinnedOrigins.Contains(statement.Origin))
            && CoveredAhead(target, 0, removed, alias, predecessors, entryBlocks, pinnedOrigins,
                [.. visited]);
    }

    /// <summary>The in-between check every statement between the removed store and its cover
    /// must pass: no fault, no interrupt delivery, no speculative or patched guard, no full
    /// memory boundary and no read of the removed bytes.</summary>
    private static bool Between(IrStatement statement, IrBlock block, int index,
            IrMemoryAccess removed, IrMemoryAliasAnalysis alias) =>
        !statement.Effects.MayFault
        && !statement.Effects.DeliversInterrupts
        && statement.Node is not SpeculativeGuardNode and not PatchedCallGuardNode
        && !alias.IsFullMemoryBoundary(block, index)
        && alias.Reads(block, index).All(read
            => alias.Relation(read, removed) == IrAliasRelation.Disjoint)
        && ValueReadsDisjoint(statement, removed, alias, block, index);

    /// <summary>The in-between check applied to a terminator: the same fault and interrupt
    /// rules, and every memory read it makes - a branch condition included - disjoint from the
    /// removed bytes.</summary>
    private static bool Between(IrTerminator terminator, IrBlock block, IrMemoryAccess removed,
            IrMemoryAliasAnalysis alias) =>
        !terminator.Effects.MayFault
        && !terminator.Effects.DeliversInterrupts
        && alias.Reads(block, terminator).All(read
            => alias.Relation(read, removed) == IrAliasRelation.Disjoint);

    /// <summary>Whether the memory reads a read-modify-write store embeds in its value part stay
    /// <see cref="IrAliasRelation.Disjoint"/> from the removed bytes. <see cref="IrMemoryAliasAnalysis.Reads"/>
    /// sees only pointer occurrences other than the store target, but a read-modify-write shares one
    /// pointer instance between the two, so the value part is scanned directly.</summary>
    private static bool ValueReadsDisjoint(IrStatement statement, IrMemoryAccess removed,
        IrMemoryAliasAnalysis alias, IrBlock block, int index) {
        if (statement.Node is not BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN }
                assign) {
            return true;
        }
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(assign.Right)) {
            if (node is SegmentedPointerNode or AbsolutePointerNode
                && alias.Relation(alias.OfPointer((ValueNode)node, statement, block, index), removed)
                    != IrAliasRelation.Disjoint) {
                return false;
            }
        }
        return true;
    }

    /// <summary>The token reads of a store target's address parts - the segment and offset of a
    /// segmented pointer, the address of an absolute one. The pointer node itself reports the
    /// memory token it writes through, which is not a read the removal erases.</summary>
    private static IrTokens AddressTokenReads(ValueNode target) => target switch {
        SegmentedPointerNode pointer => IrEffectsVisitor.Of(pointer.Segment, false).TokenReads
            | IrEffectsVisitor.Of(pointer.Offset, false).TokenReads,
        AbsolutePointerNode pointer => IrEffectsVisitor.Of(pointer.AbsoluteAddress, false)
            .TokenReads,
        _ => IrTokens.All
    };
}
