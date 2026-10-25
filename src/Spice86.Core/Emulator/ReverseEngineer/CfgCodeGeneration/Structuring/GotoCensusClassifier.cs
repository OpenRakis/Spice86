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
/// recorded refusals, the natural loops, the partition entries, the block layout), never from the printed text.
/// </summary>
internal static class GotoCensusClassifier {
    /// <summary>Classifies every <c>goto</c> of one method.</summary>
    /// <param name="entryDispatch">The entry dispatch block of the method (<c>switch (loadOffset)</c>), possibly empty.</param>
    /// <param name="body">The method body after the fallthrough <c>goto</c>s were removed.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    /// <param name="entryNodes">The partition entry nodes of the method.</param>
    /// <param name="blocks">The lowered blocks, in layout order.</param>
    /// <param name="refusals">The reasons the structurizer recorded per kept <see cref="GotoNode"/> instance.</param>
    public static GotoCensus Classify(BlockNode entryDispatch, BlockNode body, NaturalLoopForest loops,
        IReadOnlySet<ICfgNode> entryNodes, IReadOnlyList<LoweredBlock> blocks,
        IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals) {
        Walker walker = new(loops, entryNodes, blocks, refusals);
        GotoCensus census = new();
        EachGoto(entryDispatch, _ => census.Add(GotoKeepReason.EntryDispatch));
        Dictionary<ICfgNode, int> printedByTarget = new();
        EachGoto(body, jump => printedByTarget[jump.Target] = printedByTarget.GetValueOrDefault(jump.Target) + 1);
        walker.Walk(body, census, printedByTarget);
        return census;
    }

    /// <summary>Calls <paramref name="action"/> on every <see cref="GotoNode"/> of <paramref name="node"/>, in print order.</summary>
    private static void EachGoto(IVisitableAstNode node, Action<GotoNode> action) {
        switch (node) {
            case GotoNode jump:
                action(jump);
                return;
            case BlockNode block:
                foreach (IVisitableAstNode statement in block.Statements) {
                    EachGoto(statement, action);
                }
                return;
            case InstructionBoundaryNode boundary:
                EachGoto(boundary.Body, action);
                return;
            case IfElseNode ifElse:
                EachGoto(ifElse.TrueCase, action);
                EachGoto(ifElse.FalseCase, action);
                return;
            case WhileNode whileNode:
                EachGoto(whileNode.Body, action);
                return;
            case InfiniteLoopNode infiniteLoop:
                EachGoto(infiniteLoop.Body, action);
                return;
            case DoWhileNode doWhile:
                EachGoto(doWhile.Body, action);
                return;
            case SwitchNode switchNode:
                foreach (SwitchSection section in switchNode.Sections) {
                    EachGoto(section.Body, action);
                }
                EachGoto(switchNode.Default, action);
                return;
            case CpuFaultTryCatchNode tryCatch:
                EachGoto(tryCatch.Body, action);
                return;
        }
    }

    /// <summary>Walks the body once, tracking the source instruction and the enclosing structured loops.</summary>
    private sealed class Walker(NaturalLoopForest loops, IReadOnlySet<ICfgNode> entryNodes,
        IReadOnlyList<LoweredBlock> blocks, IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals) {
        private readonly List<NaturalLoop?> _enclosingLoops = [];
        private readonly Dictionary<ICfgNode, CfgBlock> _blockByNode = NodeToBlock(blocks);
        private readonly Dictionary<ICfgNode, CfgBlock> _blockByEntry = blocks.ToDictionary(block => block.Block.Entry, block => block.Block);
        private readonly Dictionary<CfgBlock, int> _layoutIndexByBlock = LayoutIndex(blocks);
        private ICfgNode? _currentNode;

        private static Dictionary<CfgBlock, int> LayoutIndex(IReadOnlyList<LoweredBlock> blocks) {
            Dictionary<CfgBlock, int> map = new();
            for (int index = 0; index < blocks.Count; index++) {
                map[blocks[index].Block] = index;
            }
            return map;
        }

        private static Dictionary<ICfgNode, CfgBlock> NodeToBlock(IReadOnlyList<LoweredBlock> blocks) {
            Dictionary<ICfgNode, CfgBlock> map = new();
            foreach (LoweredBlock block in blocks) {
                foreach (ICfgNode node in block.Block.Instructions) {
                    map[node] = block.Block;
                }
            }
            return map;
        }

        public void Walk(IVisitableAstNode node, GotoCensus census, IReadOnlyDictionary<ICfgNode, int> printedByTarget) {
            switch (node) {
                case GotoNode jump:
                    census.Add(Classify(jump, printedByTarget));
                    return;
                case CyclicJumpNode:
                    census.Add(GotoKeepReason.CyclicJump);
                    return;
                case BlockNode block:
                    foreach (IVisitableAstNode statement in block.Statements) {
                        Walk(statement, census, printedByTarget);
                    }
                    return;
                case InstructionBoundaryNode boundary: {
                    ICfgNode? outer = _currentNode;
                    _currentNode = boundary.Node;
                    Walk(boundary.Body, census, printedByTarget);
                    _currentNode = outer;
                    return;
                }
                case IfElseNode ifElse:
                    Walk(ifElse.TrueCase, census, printedByTarget);
                    Walk(ifElse.FalseCase, census, printedByTarget);
                    return;
                case WhileNode whileNode:
                    Walk(whileNode.Body, census, printedByTarget);
                    return;
                case InfiniteLoopNode infiniteLoop:
                    WalkLoop(infiniteLoop, infiniteLoop.Body, census, printedByTarget);
                    return;
                case DoWhileNode doWhile:
                    WalkLoop(doWhile, doWhile.Body, census, printedByTarget);
                    return;
                case SwitchNode switchNode:
                    foreach (SwitchSection section in switchNode.Sections) {
                        Walk(section.Body, census, printedByTarget);
                    }
                    Walk(switchNode.Default, census, printedByTarget);
                    return;
                case CpuFaultTryCatchNode tryCatch:
                    Walk(tryCatch.Body, census, printedByTarget);
                    return;
            }
        }

        private void WalkLoop(IVisitableAstNode loopNode, BlockNode body, GotoCensus census,
            IReadOnlyDictionary<ICfgNode, int> printedByTarget) {
            _enclosingLoops.Add(NaturalLoopOf(loopNode));
            Walk(body, census, printedByTarget);
            _enclosingLoops.RemoveAt(_enclosingLoops.Count - 1);
        }

        /// <summary>The natural loop a structured loop node was built from, or <c>null</c> when its header is unknown.</summary>
        private NaturalLoop? NaturalLoopOf(IVisitableAstNode loopNode) {
            ICfgNode? entry = EntryNodeOf(loopNode);
            if (entry is null || !_blockByNode.TryGetValue(entry, out CfgBlock? header)) {
                return null;
            }
            return loops.LoopWithHeader(header);
        }

        private NaturalLoop? InnermostEnclosingLoop() {
            for (int index = _enclosingLoops.Count - 1; index >= 0; index--) {
                if (_enclosingLoops[index] is not null) {
                    return _enclosingLoops[index];
                }
            }
            return null;
        }

        private GotoKeepReason Classify(GotoNode jump, IReadOnlyDictionary<ICfgNode, int> printedByTarget) {
            ICfgNode target = jump.Target;
            CfgBlock? targetBlock = _blockByEntry.GetValueOrDefault(target);
            if (targetBlock is not null && targetBlock.Instructions.Any(entryNodes.Contains)) {
                return GotoKeepReason.TargetIsEntry;
            }
            if (targetBlock is not null && loops.Loops.Any(loop => loop.Contains(targetBlock)
                && !MethodStructurizer.IsStructurable(loop, entryNodes))) {
                return GotoKeepReason.LoopWithSecondEntry;
            }
            NaturalLoop? enclosing = InnermostEnclosingLoop();
            if (enclosing is not null) {
                if (ReferenceEquals(target, enclosing.Header.Entry)) {
                    return GotoKeepReason.LoopRestart;
                }
                if (targetBlock is not null && !enclosing.Contains(targetBlock)) {
                    return GotoKeepReason.LoopExit;
                }
            }
            if (targetBlock is not null && _currentNode is not null
                && _blockByNode.TryGetValue(_currentNode, out CfgBlock? source)
                && _layoutIndexByBlock[targetBlock] < _layoutIndexByBlock.GetValueOrDefault(source, -1)
                && !loops.Dominators.Dominates(targetBlock, source)) {
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
