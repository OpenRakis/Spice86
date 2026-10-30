namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Attributes one <see cref="GotoKeepReason"/> to every <c>goto</c> the printed method holds: the <c>goto</c>s of the
/// entry dispatch, the <c>goto entrydispatcher;</c> of cyclic jumps, and every <see cref="GotoNode"/> left in the
/// body after <see cref="FallthroughGotoRemover"/>. Reasons come from the generator's own data (the structurizer's
/// recorded refusals, the natural loops, the method entries, the block layout), never from the printed text.
/// </summary>
internal static class GotoCensusClassifier {
    /// <summary>
    /// Classifies every <c>goto</c> of one method. Reasons found from the graph (entry, non-structurable loop,
    /// loop restart / exit, irreducible) take precedence over the reason the structurizer recorded, which takes
    /// precedence over the fallbacks <see cref="GotoKeepReason.SharedTarget"/> and <see cref="GotoKeepReason.NotNextInLayout"/>.
    /// </summary>
    /// <param name="entryDispatch">The entry dispatch block of the method (<c>switch (loadOffset)</c>), possibly empty.</param>
    /// <param name="body">The method body after the fallthrough <c>goto</c>s were removed.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    /// <param name="entryTargets">The goto targets that are method entries.</param>
    /// <param name="blocks">The lowered blocks, in layout order.</param>
    /// <param name="nodesOf">The CFG nodes each block holds; identifies which block a boundary's node belongs to.</param>
    /// <param name="refusals">The reasons the structurizer recorded per kept <see cref="GotoNode"/> instance.</param>
    public static GotoCensus Classify<TBlock>(BlockNode entryDispatch, BlockNode body, NaturalLoopForest<TBlock> loops,
        IReadOnlySet<IGotoTarget> entryTargets, IReadOnlyList<LoweredBlock<TBlock>> blocks,
        Func<TBlock, IEnumerable<ICfgNode>> nodesOf,
        IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals) where TBlock : class {
        Walker<TBlock> walker = new(loops, entryTargets, blocks, nodesOf, refusals);
        GotoCensus census = new();
        census.Add(GotoKeepReason.EntryDispatch, Gotos(entryDispatch).Count());
        Dictionary<IGotoTarget, int> printedByTarget = Gotos(body).CountBy(jump => jump.Target)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        walker.Walk(body, census, printedByTarget);
        return census;
    }

    /// <summary>Walks the body once, tracking the source instruction and the enclosing structured loops.</summary>
    private sealed class Walker<TBlock>(NaturalLoopForest<TBlock> loops, IReadOnlySet<IGotoTarget> entryTargets,
        IReadOnlyList<LoweredBlock<TBlock>> blocks, Func<TBlock, IEnumerable<ICfgNode>> nodesOf,
        IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals)
        where TBlock : class {
        private readonly List<NaturalLoop<TBlock>?> _enclosingLoops = [];
        private readonly Dictionary<IGotoTarget, LoweredBlock<TBlock>> _memberByEntry =
            blocks.ToDictionary(block => block.Entry);
        private readonly Dictionary<TBlock, LoweredBlock<TBlock>> _memberByBlock =
            blocks.ToDictionary(block => block.Block);
        private readonly Dictionary<ICfgNode, TBlock> _blockByNode = NodeToBlock(blocks, nodesOf);
        private readonly Dictionary<TBlock, int> _layoutIndexByBlock = LayoutIndex(blocks);
        private ICfgNode? _currentNode;

        private static Dictionary<TBlock, int> LayoutIndex(IReadOnlyList<LoweredBlock<TBlock>> blocks) {
            Dictionary<TBlock, int> map = new();
            for (int index = 0; index < blocks.Count; index++) {
                map[blocks[index].Block] = index;
            }
            return map;
        }

        private static Dictionary<ICfgNode, TBlock> NodeToBlock(IReadOnlyList<LoweredBlock<TBlock>> blocks,
            Func<TBlock, IEnumerable<ICfgNode>> nodesOf) {
            Dictionary<ICfgNode, TBlock> map = new();
            foreach (LoweredBlock<TBlock> block in blocks) {
                foreach (ICfgNode node in nodesOf(block.Block)) {
                    map[node] = block.Block;
                }
            }
            return map;
        }

        public void Walk(IVisitableAstNode node, GotoCensus census, IReadOnlyDictionary<IGotoTarget, int> printedByTarget) {
            AstTrampoline.Run(WalkIt(node, census, printedByTarget));
        }

        private IEnumerable<AstSubcall> WalkIt(IVisitableAstNode node, GotoCensus census,
            IReadOnlyDictionary<IGotoTarget, int> printedByTarget) {
            switch (node) {
                case GotoNode jump:
                    census.Add(Classify(jump, printedByTarget));
                    yield break;
                case CyclicJumpNode:
                    census.Add(GotoKeepReason.CyclicJump);
                    yield break;
                case InstructionBoundaryNode boundary: {
                    ICfgNode? outer = _currentNode;
                    _currentNode = boundary.Node;
                    yield return AstTrampoline.Call(WalkIt(boundary.Body, census, printedByTarget));
                    _currentNode = outer;
                    yield break;
                }
                case InfiniteLoopNode infiniteLoop:
                    yield return AstTrampoline.Call(WalkLoopIt(infiniteLoop, infiniteLoop.Body, census, printedByTarget));
                    yield break;
                case DoWhileNode doWhile:
                    yield return AstTrampoline.Call(WalkLoopIt(doWhile, doWhile.Body, census, printedByTarget));
                    yield break;
                case WhileNode whileLoop:
                    yield return AstTrampoline.Call(WalkLoopIt(whileLoop, whileLoop.Body, census, printedByTarget));
                    yield break;
                default:
                    foreach (IVisitableAstNode child in ChildStatements(node)) {
                        yield return AstTrampoline.Call(WalkIt(child, census, printedByTarget));
                    }
                    yield break;
            }
        }

        private IEnumerable<AstSubcall> WalkLoopIt(IVisitableAstNode loopNode, BlockNode body,
            GotoCensus census, IReadOnlyDictionary<IGotoTarget, int> printedByTarget) {
            _enclosingLoops.Add(NaturalLoopOf(loopNode));
            try {
                yield return AstTrampoline.Call(WalkIt(body, census, printedByTarget));
            } finally {
                _enclosingLoops.RemoveAt(_enclosingLoops.Count - 1);
            }
        }

        /// <summary>The natural loop a structured loop node was built from, or <c>null</c> when its header is unknown.</summary>
        private NaturalLoop<TBlock>? NaturalLoopOf(IVisitableAstNode loopNode) {
            IGotoTarget? entry = AnchorOf(loopNode);
            if (entry is null || !_memberByEntry.TryGetValue(entry, out LoweredBlock<TBlock>? header)) {
                return null;
            }
            return loops.LoopWithHeader(header.Block);
        }

        private NaturalLoop<TBlock>? InnermostEnclosingLoop() {
            for (int index = _enclosingLoops.Count - 1; index >= 0; index--) {
                if (_enclosingLoops[index] is not null) {
                    return _enclosingLoops[index];
                }
            }
            return null;
        }

        private GotoKeepReason Classify(GotoNode jump, IReadOnlyDictionary<IGotoTarget, int> printedByTarget) {
            IGotoTarget target = jump.Target;
            LoweredBlock<TBlock>? targetMember = _memberByEntry.GetValueOrDefault(target);
            if (targetMember is not null && targetMember.HoldsEntry) {
                return GotoKeepReason.TargetIsEntry;
            }
            if (targetMember is not null && loops.Loops.Any(loop => loop.Contains(targetMember.Block)
                && !MethodStructurizer.IsStructurable(loop, _memberByBlock, entryTargets))) {
                return GotoKeepReason.LoopWithSecondEntry;
            }
            NaturalLoop<TBlock>? enclosing = InnermostEnclosingLoop();
            if (enclosing is not null) {
                if (_memberByBlock.TryGetValue(enclosing.Header, out LoweredBlock<TBlock>? headerMember)
                    && Equals(target, headerMember.Entry)) {
                    return GotoKeepReason.LoopRestart;
                }
                if (targetMember is not null && !enclosing.Contains(targetMember.Block)) {
                    return GotoKeepReason.LoopExit;
                }
            }
            if (targetMember is not null && _currentNode is not null
                && _blockByNode.TryGetValue(_currentNode, out TBlock? source)
                && _layoutIndexByBlock[targetMember.Block] < _layoutIndexByBlock.GetValueOrDefault(source, -1)
                && !loops.Dominators.Dominates(targetMember.Block, source)) {
                return GotoKeepReason.Irreducible;
            }
            if (refusals.TryGetValue(jump, out GotoKeepReason reason)) {
                return reason;
            }
            if (printedByTarget.GetValueOrDefault(target) >= 2) {
                return GotoKeepReason.SharedTarget;
            }
            return GotoKeepReason.NotNextInLayout;
        }
    }
}
