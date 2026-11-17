namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Removes a store whose every byte a later store of the same block provably rewrites before
/// anything can read it: <c>mov [si],ax</c> followed by <c>mov [si],bx</c> keeps only the second
/// store. Legality rows, all through <see cref="IrMemoryAliasAnalysis"/>:
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
/// </summary>
internal static class IrOverwrittenStores {
    /// <summary>Removes fully covered stores, block by block; returns the removed count.</summary>
    /// <summary>Applies the pass in strict mode.</summary>
    public static int Apply(IrMethod method, IrEditor editor) =>
        Apply(method, editor, GenerationPolicy.Strict);

    public static int Apply(IrMethod method, IrEditor editor, GenerationPolicy policy) {
        IrSsa ssa = editor.Ssa;
        IrMemoryAliasAnalysis alias = new(method, ssa, policy);
        List<(IrBlock Block, int Index)> removable = [];
        foreach (IrBlock block in method.Blocks) {
            for (int s = 0; s < block.Statements.Count; s++) {
                if (Removable(block, s, alias)) {
                    removable.Add((block, s));
                }
            }
        }
        foreach ((IrBlock block, int index) in removable.OrderByDescending(pair => pair.Index)) {
            editor.Remove(block, index);
        }
        return removable.Count;
    }

    /// <summary>Whether the statement at <paramref name="s"/> is a plain store a later statement
    /// of <paramref name="block"/> fully covers under the legality rows: non-faulting, no token
    /// reads in its address or value, proved RAM, and a non-faulting covering write ahead with
    /// only disjoint reads in between.</summary>
    private static bool Removable(IrBlock block, int s, IrMemoryAliasAnalysis alias) {
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
        for (int k = s + 1; k < block.Statements.Count; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Effects.DeliversInterrupts
                || between.Node is SpeculativeGuardNode or PatchedCallGuardNode
                || alias.IsFullMemoryBoundary(block, k)
                || alias.Reads(block, k).Any(read
                    => alias.Relation(read, storeAccess) != IrAliasRelation.Disjoint)
                || !ValueReadsDisjoint(between, storeAccess, alias, block, k)) {
                return false;
            }
            if (alias.Writes(block, k) is [IrMemoryAccess cover]
                && alias.Covers(cover, storeAccess)) {
                return true;
            }
        }
        return false;
    }

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
