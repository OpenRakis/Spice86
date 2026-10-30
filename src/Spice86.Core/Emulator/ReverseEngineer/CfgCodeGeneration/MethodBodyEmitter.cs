namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// The shared structuring tail of both emitters: from the lowered blocks to the labelled body, the
/// goto census and the goto targets. Moving a loop exit after its loop can break fall-throughs of the
/// blocks it jumps over, so when the move happened the method is also structured without it and the result
/// with fewer gotos is kept.
/// </summary>
internal static class MethodBodyEmitter {
    /// <summary>Structures <paramref name="loweredBlocks"/> and returns the labelled body, the goto census and the goto targets.</summary>
    /// <param name="entryDispatch">The entry dispatch block of the method, possibly empty.</param>
    /// <param name="nodesOf">The CFG nodes each block holds; identifies which block a boundary's node belongs to.</param>
    /// <param name="localsDeclaredInDominatingBlocks">Whether locals declared in a block that dominates
    /// the branch's block count as declared for condition nesting.</param>
    /// <exception cref="InvalidOperationException">The structured body can fall off its end.</exception>
    public static (BlockNode Body, GotoCensus Census, GotoTargets Targets) Structure<TBlock>(
        BlockNode entryDispatch,
        IReadOnlyList<LoweredBlock<TBlock>> loweredBlocks,
        NaturalLoopForest<TBlock> loops,
        string methodName,
        HashSet<IGotoTarget> entryTargets,
        Func<TBlock, IEnumerable<ICfgNode>> nodesOf,
        bool allowLocalsDeclaredInBlock,
        bool localsDeclaredInDominatingBlocks) where TBlock : class {
        MethodStructurizer.Inputs<TBlock> inputs = MethodStructurizer.PrepareInputs(loweredBlocks,
            loops, methodName, localsDeclaredInDominatingBlocks);
        Dictionary<GotoNode, GotoKeepReason> placedRefusals = new(ReferenceEqualityComparer.Instance);
        (BlockNode placedWalked, bool movedLoopExit) = MethodStructurizer.Structure(loweredBlocks, loops,
            methodName, entryTargets, placedRefusals, placeLoopExits: true, allowLocalsDeclaredInBlock, inputs);
        Dictionary<GotoNode, GotoKeepReason> refusals = placedRefusals;
        BlockNode body = FallthroughGotoRemover.Remove(placedWalked);
        if (movedLoopExit) {
            Dictionary<GotoNode, GotoKeepReason> unplacedRefusals = new(ReferenceEqualityComparer.Instance);
            BlockNode unplacedBody = FallthroughGotoRemover.Remove(
                MethodStructurizer.Structure(loweredBlocks, loops, methodName, entryTargets, unplacedRefusals,
                    placeLoopExits: false, allowLocalsDeclaredInBlock, inputs).Body);
            if (MethodStructurizer.TotalGotos(body) >= MethodStructurizer.TotalGotos(unplacedBody)) {
                body = unplacedBody;
                refusals = unplacedRefusals;
            }
        }
        body = TailDuplicator.Apply(body, loweredBlocks, loops);
        HashSet<IGotoTarget> jumpTargets = new(GotoTargetCollector.Collect(body).Labels);
        jumpTargets.UnionWith(entryTargets);
        body = ConditionCombiner.Apply(body, jumpTargets);
        GotoCensus census = GotoCensusClassifier.Classify(entryDispatch, body, loops,
            entryTargets, loweredBlocks, nodesOf, refusals);
        body = WhileConditionLoops.Apply(body);
        GotoTargets targets = GotoTargetCollector.Collect(Sequence(entryDispatch, body));
        body = LabelPlacer.Place(body, targets.Labels);
        LabelScopeCheck.Verify(Sequence(entryDispatch, body));
        // The last node has no next node, so every same-method fallthrough from it is a goto, and an
        // unobserved fallthrough is a throw: a body that can still fall off its end is a generator bug.
        if (CompletesNormallyVisitor.CompletesNormally(body)) {
            throw new InvalidOperationException($"Generated method {methodName} can fall off its end: its last node does not diverge.");
        }
        return (body, census, targets);
    }
}
