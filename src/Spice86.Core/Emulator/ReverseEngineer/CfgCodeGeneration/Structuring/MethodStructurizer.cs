namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Builds the body of a generated method from its lowered blocks. Every transfer of the blocks is still an explicit
/// <c>goto</c>, so the order and nesting chosen here only change readability; <c>FallthroughGotoRemover</c> runs on
/// the result.
/// </summary>
internal static class MethodStructurizer {
    /// <summary>
    /// The method body: the lowered blocks in layout order, every natural loop gathered into a <c>do</c>, a
    /// <c>while (c)</c> or a <c>while (true)</c> loop at its header's place, child loops inside their parent's body,
    /// and a forward branch whose arms are entered only from it nested in an <c>if</c> (one arm that comes back to
    /// the other target) or an <c>if</c> / <c>else</c> (two arms that meet at one join), and the arms of a near jump
    /// table nested in their <c>case</c> sections.
    /// </summary>
    /// <param name="blocks">The lowered blocks, in layout order.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    /// <param name="methodName">The method name, for error messages.</param>
    /// <param name="entryTargets">The goto targets that are method entries (the entry block); a loop
    /// holding one other than its header's cannot be a structured loop.</param>
    /// <param name="refusals">Filled with the reason each <c>goto</c> arm of a refused branch or <c>switch</c> section
    /// was not nested, keyed by the <see cref="GotoNode"/> instance (later records overwrite earlier ones).</param>
    /// <param name="placeLoopExits">When set, the exit of a loop that the loop's <c>goto</c>s target the most is moved
    /// right after the loop in the printing order, so the <c>goto</c>s to it become <c>break</c>s.</param>
    /// <param name="allowLocalsDeclaredInBlock">When set, a branch whose condition reads local variables may still
    /// nest, exactly when every variable it reads is declared by a statement of the same lowered block.</param>
    /// <param name="localsDeclaredInDominatingBlocks">When set (with <paramref name="allowLocalsDeclaredInBlock"/>),
    /// a variable declared by a statement of a block that dominates the branch's block in the block graph counts as
    /// declared for the branch's condition: the branch's blocks nest inside that block's region, so the declaration
    /// stays in scope where the condition ends up.</param>
    /// <exception cref="InvalidOperationException">A natural loop header needs but does not start with <c>CheckExternalEvents</c>.</exception>
    /// <exception cref="InvalidOperationException">A loop header or an arm head is not the first block of its loop or arm in the layout.</exception>
    public static BlockNode Structure<TBlock>(IReadOnlyList<LoweredBlock<TBlock>> blocks, NaturalLoopForest<TBlock> loops,
        string methodName, IReadOnlySet<IGotoTarget> entryTargets, Dictionary<GotoNode, GotoKeepReason> refusals,
        bool placeLoopExits, bool allowLocalsDeclaredInBlock, bool localsDeclaredInDominatingBlocks) where TBlock : class =>
        Structure(blocks, loops, methodName, entryTargets, refusals, placeLoopExits,
            allowLocalsDeclaredInBlock,
            PrepareInputs(blocks, loops, methodName, localsDeclaredInDominatingBlocks)).Body;

    /// <summary>The checks and indexes <see cref="Structure{TBlock}(IReadOnlyList{LoweredBlock{TBlock}},
    /// NaturalLoopForest{TBlock}, string, IReadOnlySet{IGotoTarget}, Dictionary{GotoNode, GotoKeepReason},
    /// bool, bool, Inputs{TBlock})"/> needs, computed once when the same lowered blocks are structured
    /// several ways.</summary>
    internal sealed class Inputs<TBlock> where TBlock : class {
        /// <summary>The goto count of every goto target of the lowered bodies.</summary>
        public required IReadOnlyDictionary<IGotoTarget, int> GotoCountByTarget { get; init; }
        /// <summary>The lowered block of each block.</summary>
        public required IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> MemberByBlock { get; init; }
        /// <summary>The locals each block sees declared by its strictly dominating blocks.</summary>
        public required IReadOnlyDictionary<TBlock, HashSet<string>> DeclaredInDominating { get; init; }
        /// <summary>The block whose entry is each goto target of the lowered bodies.</summary>
        public required IReadOnlyDictionary<IGotoTarget, TBlock> BlockByEntry { get; init; }
        /// <summary>The position of each block in the top-level layout.</summary>
        public required IReadOnlyDictionary<TBlock, int> PositionByBlock { get; init; }
        /// <summary>The graph predecessors of each block that are reachable in the dominator tree,
        /// sorted by dominator-tree pre-order index.</summary>
        public required IReadOnlyDictionary<TBlock, (int Preorder, TBlock Block)[]> ReachablePredecessors { get; init; }
        /// <summary>The number of lowered blocks in each reachable block's dominator subtree.</summary>
        public required IReadOnlyDictionary<TBlock, int> SubtreeMemberCount { get; init; }
        /// <summary>The earliest top-level layout position among the lowered blocks of each reachable
        /// block's dominator subtree, or <see cref="int.MaxValue"/> when it holds none.</summary>
        public required IReadOnlyDictionary<TBlock, int> SubtreeFirstPosition { get; init; }
    }

    /// <summary>Runs the loop-header check and builds the indexes shared by every structuring of
    /// <paramref name="blocks"/>.</summary>
    public static Inputs<TBlock> PrepareInputs<TBlock>(IReadOnlyList<LoweredBlock<TBlock>> blocks,
        NaturalLoopForest<TBlock> loops, string methodName,
        bool localsDeclaredInDominatingBlocks) where TBlock : class {
        CheckLoopHeaders(blocks, loops, methodName);
        Dictionary<TBlock, LoweredBlock<TBlock>> memberByBlock = blocks.ToDictionary(block => block.Block);
        Dictionary<TBlock, int> positionByBlock = blocks.Select((member, index) => (member.Block, index))
            .ToDictionary(pair => pair.Block, pair => pair.index);
        (Dictionary<TBlock, int> count, Dictionary<TBlock, int> firstPosition) =
            SubtreeStats(memberByBlock, positionByBlock, loops);
        return new Inputs<TBlock> {
            GotoCountByTarget = blocks.SelectMany(block => Gotos(block.Body))
                .CountBy(jump => jump.Target).ToDictionary(pair => pair.Key, pair => pair.Value),
            MemberByBlock = memberByBlock,
            DeclaredInDominating = localsDeclaredInDominatingBlocks
                ? RegionWalker<TBlock>.DeclaredInDominatingBlocks(blocks, loops.Dominators)
                : new Dictionary<TBlock, HashSet<string>>(),
            BlockByEntry = blocks.ToDictionary(member => member.Entry, member => member.Block),
            PositionByBlock = positionByBlock,
            ReachablePredecessors = ReachablePredecessorsByPreorder(blocks, loops),
            SubtreeMemberCount = count,
            SubtreeFirstPosition = firstPosition
        };
    }

    /// <summary>The number of lowered blocks of <paramref name="memberByBlock"/> in each reachable block's
    /// dominator subtree, and the earliest of their top-level layout positions, computed bottom-up over the
    /// dominator-tree pre-order.</summary>
    private static (Dictionary<TBlock, int> Count, Dictionary<TBlock, int> FirstPosition) SubtreeStats<TBlock>(
        IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> memberByBlock,
        IReadOnlyDictionary<TBlock, int> positionByBlock, NaturalLoopForest<TBlock> loops) where TBlock : class {
        IReadOnlyList<TBlock> preorder = loops.Dominators.PreorderBlocks;
        int[] counts = new int[preorder.Count];
        int[] firsts = new int[preorder.Count];
        for (int index = 0; index < preorder.Count; index++) {
            if (memberByBlock.ContainsKey(preorder[index])) {
                counts[index] = 1;
                firsts[index] = positionByBlock[preorder[index]];
            } else {
                firsts[index] = int.MaxValue;
            }
        }
        for (int index = preorder.Count - 1; index > 0; index--) {
            TBlock? parent = loops.Dominators.GetImmediateDominator(preorder[index]);
            if (parent is null) {
                continue;
            }
            int parentIndex = loops.Dominators.PreorderIndex(parent);
            if (parentIndex < 0) {
                continue;
            }
            counts[parentIndex] += counts[index];
            if (firsts[index] < firsts[parentIndex]) {
                firsts[parentIndex] = firsts[index];
            }
        }
        Dictionary<TBlock, int> count = [];
        Dictionary<TBlock, int> firstPosition = [];
        for (int index = 0; index < preorder.Count; index++) {
            count[preorder[index]] = counts[index];
            firstPosition[preorder[index]] = firsts[index];
        }
        return (count, firstPosition);
    }

    /// <summary>The graph predecessors of each block of <paramref name="blocks"/> that have a
    /// dominator-tree pre-order index, sorted by that index.</summary>
    private static IReadOnlyDictionary<TBlock, (int Preorder, TBlock Block)[]> ReachablePredecessorsByPreorder<TBlock>(
        IReadOnlyList<LoweredBlock<TBlock>> blocks, NaturalLoopForest<TBlock> loops) where TBlock : class {
        Dictionary<TBlock, (int Preorder, TBlock Block)[]> result = [];
        foreach (LoweredBlock<TBlock> member in blocks) {
            result[member.Block] = loops.Graph.GetPredecessors(member.Block)
                .Select(predecessor => (Preorder: loops.Dominators.PreorderIndex(predecessor), Block: predecessor))
                .Where(pair => pair.Preorder >= 0)
                .OrderBy(pair => pair.Preorder)
                .ToArray();
        }
        return result;
    }

    /// <summary>Structures <paramref name="blocks"/> reusing the <paramref name="inputs"/> of
    /// <see cref="PrepareInputs{TBlock}"/>.</summary>
    /// <returns>The structured body, and whether placing loop exits changed the layout order
    /// (when <paramref name="placeLoopExits"/> is unset the flag is always <c>false</c>).</returns>
    internal static (BlockNode Body, bool MovedLoopExit) Structure<TBlock>(IReadOnlyList<LoweredBlock<TBlock>> blocks,
        NaturalLoopForest<TBlock> loops,
        string methodName, IReadOnlySet<IGotoTarget> entryTargets, Dictionary<GotoNode, GotoKeepReason> refusals,
        bool placeLoopExits, bool allowLocalsDeclaredInBlock, Inputs<TBlock> inputs) where TBlock : class {
        RegionWalker<TBlock> walker = new(loops, entryTargets, inputs.GotoCountByTarget, refusals, methodName,
            placeLoopExits, allowLocalsDeclaredInBlock, inputs.MemberByBlock, inputs.DeclaredInDominating,
            inputs.BlockByEntry, inputs.PositionByBlock, inputs.ReachablePredecessors,
            inputs.SubtreeMemberCount, inputs.SubtreeFirstPosition);
        return (walker.Walk(blocks, null), walker.MovedLoopExit);
    }

    /// <summary>The number of <see cref="GotoNode"/>s anywhere in <paramref name="node"/>.</summary>
    internal static int TotalGotos(IVisitableAstNode node) => Gotos(node).Count();

    /// <summary>
    /// Whether <paramref name="loop"/> can become a structured loop: no block of it other than its header holds a
    /// method entry, and the header holds no entry other than its own.
    /// </summary>
    internal static bool IsStructurable<TBlock>(NaturalLoop<TBlock> loop,
        IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> memberByBlock,
        IReadOnlySet<IGotoTarget> entryTargets) where TBlock : class {
        IGotoTarget? headerEntry = memberByBlock.GetValueOrDefault(loop.Header)?.Entry;
        return !loop.Blocks.Any(block => memberByBlock.TryGetValue(block, out LoweredBlock<TBlock>? member)
            && (!ReferenceEquals(block, loop.Header) && member.HoldsEntry
                || HoldsEntryBoundaryOtherThanHeader(member, headerEntry, entryTargets)));
    }

    /// <summary>An instruction boundary of <paramref name="member"/> is a method entry other than the header's own.</summary>
    private static bool HoldsEntryBoundaryOtherThanHeader<TBlock>(LoweredBlock<TBlock> member, IGotoTarget? headerEntry,
        IReadOnlySet<IGotoTarget> entryTargets) where TBlock : class =>
        member.Body.Statements.OfType<InstructionBoundaryNode>()
            .Select(boundary => boundary.Anchor)
            .Any(anchor => anchor is not null && !Equals(anchor, headerEntry) && entryTargets.Contains(anchor));

    private static GotoNode? OnlyGoto(IVisitableAstNode arm) =>
        StatementsOf(arm) is [GotoNode jump] ? jump : null;

    private static IGotoTarget? OnlyGotoTarget(IVisitableAstNode arm) => OnlyGoto(arm)?.Target;

    private static bool IsOnlyGotoTo(IVisitableAstNode arm, IGotoTarget target) =>
        StatementsOf(arm) is [GotoNode jump] && Equals(jump.Target, target);

    /// <summary>
    /// The statement after a <c>do</c> loop, when the exit arm is a single <c>goto</c> that does not restart the loop,
    /// or a <c>FailAsUntested</c> whose message reads no value, so it can move after the loop.
    /// </summary>
    private static IVisitableAstNode? ExitStatement(IVisitableAstNode arm, IGotoTarget header) {
        if (StatementsOf(arm) is not [IVisitableAstNode single]) {
            return null;
        }
        if (single is GotoNode jump && !Equals(jump.Target, header)) {
            return jump;
        }
        if (single is FailAsUntestedNode failure && failure.Parts.All(part => part.Value is null)) {
            return failure;
        }
        return null;
    }

    /// <summary>
    /// The last statement of the last instruction boundary of <paramref name="block"/> when it is a <typeparamref name="T"/>,
    /// else <c>null</c>.
    /// </summary>
    private static T? FinalStatement<T>(BlockNode block) where T : class, IVisitableAstNode {
        if (block.Statements is [.., InstructionBoundaryNode last]
            && last.Body.Statements is [.., T final]) {
            return final;
        }
        return null;
    }

    /// <summary>
    /// The statements of <paramref name="block"/> except the last, then the last instruction boundary without its last
    /// statement.
    /// </summary>
    private static List<IVisitableAstNode> HeadBefore(BlockNode block) {
        if (block.Statements is not [.., InstructionBoundaryNode last]) {
            return [.. block.Statements];
        }
        InstructionBoundaryNode lastWithoutFinal = last with {
            Body = Sequence(last.Body.Statements.Take(last.Body.Statements.Count - 1))
        };
        return [.. block.Statements.Take(block.Statements.Count - 1), lastWithoutFinal];
    }

    private static void CheckLoopHeaders<TBlock>(IReadOnlyList<LoweredBlock<TBlock>> blocks, NaturalLoopForest<TBlock> loops,
        string methodName) where TBlock : class {
        Dictionary<TBlock, LoweredBlock<TBlock>> memberByBlock = blocks.ToDictionary(block => block.Block);
        foreach (NaturalLoop<TBlock> loop in loops.Loops) {
            if (memberByBlock.TryGetValue(loop.Header, out LoweredBlock<TBlock>? header)
                && header.LoopHeaderNeedsCheck && !header.HasEntryCheck) {
                throw new InvalidOperationException(
                    $"Generated method {methodName}: loop header {header.Entry} has no CheckExternalEvents at its entry.");
            }
        }
    }

    /// <summary>The parts of a <c>do</c> loop: the latch block without its final <c>if</c>, the loop condition and the statement after the loop.</summary>
    private sealed record DoWhileShape(BlockNode LatchBody, ValueNode Condition, IVisitableAstNode Exit);

    /// <summary>
    /// The blocks one <see cref="RegionWalker.Walk"/> call places, in layout order, indexed by block. A region can
    /// also be the whole dominator subtree of a head block, lazy: its members are the lowered blocks of the
    /// subtree.
    /// </summary>
    private sealed class Region<TBlock> where TBlock : class {
        private readonly IReadOnlyDictionary<IGotoTarget, TBlock> _blockByEntry;
        private readonly IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> _memberByBlock;
        private readonly IReadOnlyDictionary<TBlock, int> _positionByBlock;
        private readonly DominatorTree<TBlock>? _dominators;

        public Region(IReadOnlyList<LoweredBlock<TBlock>> members,
            IReadOnlyDictionary<IGotoTarget, TBlock> blockByEntry,
            IReadOnlyDictionary<TBlock, int> positionByBlock) {
            MemberCount = members.Count;
            _memberByBlock = members.ToDictionary(member => member.Block);
            _blockByEntry = blockByEntry;
            _positionByBlock = positionByBlock;
        }

        private Region(TBlock subtreeHead, int memberCount, DominatorTree<TBlock> dominators,
            IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> memberByBlock,
            IReadOnlyDictionary<IGotoTarget, TBlock> blockByEntry,
            IReadOnlyDictionary<TBlock, int> positionByBlock) {
            SubtreeHead = subtreeHead;
            MemberCount = memberCount;
            _dominators = dominators;
            _memberByBlock = memberByBlock;
            _blockByEntry = blockByEntry;
            _positionByBlock = positionByBlock;
        }

        /// <summary>The region that is the full dominator subtree of <paramref name="head"/>: its members are
        /// the lowered blocks of the subtree.</summary>
        public static Region<TBlock> FullSubtree(TBlock head, int memberCount, DominatorTree<TBlock> dominators,
            IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> memberByBlock,
            IReadOnlyDictionary<IGotoTarget, TBlock> blockByEntry,
            IReadOnlyDictionary<TBlock, int> positionByBlock) =>
            new(head, memberCount, dominators, memberByBlock, blockByEntry, positionByBlock);

        /// <summary>The head of the subtree the region is, or <c>null</c> for a materialized region.</summary>
        public TBlock? SubtreeHead { get; }

        /// <summary>The number of members.</summary>
        public int MemberCount { get; }

        /// <summary>The member whose entry is <paramref name="entry"/>, or <c>null</c>.</summary>
        public LoweredBlock<TBlock>? MemberWithEntry(IGotoTarget entry) =>
            _blockByEntry.TryGetValue(entry, out TBlock? block) ? MemberOf(block) : null;

        /// <summary>The member whose block is <paramref name="block"/>, or <c>null</c>.</summary>
        public LoweredBlock<TBlock>? MemberOf(TBlock block) {
            if (SubtreeHead is null) {
                return _memberByBlock.TryGetValue(block, out LoweredBlock<TBlock>? member) ? member : null;
            }
            return _dominators!.Dominates(SubtreeHead, block)
                ? _memberByBlock.GetValueOrDefault(block) : null;
        }

        /// <summary>The position of <paramref name="member"/> in the top-level layout order.</summary>
        public int LayoutIndex(LoweredBlock<TBlock> member) => _positionByBlock[member.Block];
    }

    /// <summary>
    /// The blocks of one arm of a branch: a list, or a head plus the count of the lowered blocks of its dominator
    /// subtree when the arm is that whole subtree, materialized only when <see cref="Members"/> is read.
    /// </summary>
    private sealed class Arm<TBlock>(LoweredBlock<TBlock> head, int count,
        List<LoweredBlock<TBlock>>? members, Func<List<LoweredBlock<TBlock>>> materialize) where TBlock : class {
        private List<LoweredBlock<TBlock>>? _members = members;

        /// <summary>The first member of the arm in layout order.</summary>
        public LoweredBlock<TBlock> Head { get; } = head;

        /// <summary>The number of members.</summary>
        public int Count { get; } = count;

        /// <summary>Whether the arm is the whole lowered dominator subtree of its head.</summary>
        public bool IsFullSubtree { get; } = members is null;

        /// <summary>The members in layout order; materializes the subtree for a lazy arm.</summary>
        public IReadOnlyList<LoweredBlock<TBlock>> Members => _members ??= materialize();
    }

    /// <summary>
    /// The blocks a <see cref="RegionWalker.Walk"/> call already placed: an explicit set plus the dominator
    /// subtrees of lazily nested arms, held as their contiguous pre-order ranges.
    /// </summary>
    private sealed class PlacedBlocks<TBlock>(DominatorTree<TBlock> dominators,
        IReadOnlyDictionary<TBlock, int> subtreeMemberCount) where TBlock : class {
        private readonly HashSet<TBlock> _explicit = [];
        private readonly List<(int Start, int End)> _subtreeRanges = [];
        private int _subtreeCount;

        /// <summary>The number of placed blocks.</summary>
        public int Count => _explicit.Count + _subtreeCount;

        /// <summary>Whether <paramref name="block"/> is placed.</summary>
        public bool Contains(TBlock block) {
            if (_explicit.Contains(block)) {
                return true;
            }
            if (_subtreeRanges.Count == 0) {
                return false;
            }
            int index = dominators.PreorderIndex(block);
            if (index < 0) {
                return false;
            }
            foreach ((int start, int end) in _subtreeRanges) {
                if (index >= start && index < end) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The exclusive end of the placed subtree range that starts at pre-order index
        /// <paramref name="start"/>, when one exists.</summary>
        public bool TryGetSubtreeRangeEnd(int start, out int end) {
            foreach ((int rangeStart, int rangeEnd) in _subtreeRanges) {
                if (rangeStart == start) {
                    end = rangeEnd;
                    return true;
                }
            }
            end = 0;
            return false;
        }

        public void Add(TBlock block) => _explicit.Add(block);

        public void UnionWith(IEnumerable<TBlock> blocks) => _explicit.UnionWith(blocks);

        /// <summary>Places the whole dominator subtree of <paramref name="head"/>.</summary>
        public void AddSubtree(TBlock head) {
            _subtreeRanges.Add(dominators.SubtreeRange(head));
            _subtreeCount += subtreeMemberCount[head];
        }
    }

    /// <summary>Structures the regions of one method: the method body, loop bodies and <c>if</c> arms.</summary>
    private sealed class RegionWalker<TBlock>(NaturalLoopForest<TBlock> loops,
        IReadOnlySet<IGotoTarget> entryTargets, IReadOnlyDictionary<IGotoTarget, int> gotoCountByTarget,
        Dictionary<GotoNode, GotoKeepReason> refusals, string methodName, bool placeLoopExits,
        bool allowLocalsDeclaredInBlock, IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> memberByBlock,
        IReadOnlyDictionary<TBlock, HashSet<string>> declaredInDominating,
        IReadOnlyDictionary<IGotoTarget, TBlock> blockByEntry,
        IReadOnlyDictionary<TBlock, int> positionByBlock,
        IReadOnlyDictionary<TBlock, (int Preorder, TBlock Block)[]> reachablePredecessors,
        IReadOnlyDictionary<TBlock, int> subtreeMemberCount,
        IReadOnlyDictionary<TBlock, int> subtreeFirstPosition) where TBlock : class {
        private readonly IReadOnlyDictionary<TBlock, LoweredBlock<TBlock>> _memberByBlock = memberByBlock;
        private readonly IReadOnlyDictionary<TBlock, HashSet<string>> _declaredInDominatingBlocks = declaredInDominating;

        /// <summary>Whether <see cref="MoveExitAfterLoop"/> changed the layout order.</summary>
        internal bool MovedLoopExit { get; private set; }
        /// <summary>
        /// The statements of <paramref name="members"/>, blocks in layout order: the header of a structurable loop other
        /// than <paramref name="enclosing"/> starts that loop, built from its blocks among <paramref name="members"/>; a
        /// block that ends with a forward branch may get one arm nested in an <c>if</c>.
        /// </summary>
        /// <param name="enclosing">The loop whose body <paramref name="members"/> are part of, or <c>null</c> outside every loop.</param>
        public BlockNode Walk(IReadOnlyList<LoweredBlock<TBlock>> members, NaturalLoop<TBlock>? enclosing) {
            AstResult<BlockNode> result = new();
            AstTrampoline.Run(WalkIt(members, enclosing, result));
            return result.Value!;
        }

        private IEnumerable<AstSubcall> WalkIt(IReadOnlyList<LoweredBlock<TBlock>> members,
            NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            Region<TBlock> region = new(members, blockByEntry, positionByBlock);
            PlacedBlocks<TBlock> placed = new(loops.Dominators, subtreeMemberCount);
            List<IVisitableAstNode> statements = [];
            yield return AstTrampoline.Call(WalkBlocksIt([.. members], region, placed, statements, enclosing));
            result.Value = Sequence(statements);
        }

        /// <summary>The lowered blocks of the dominator subtree of <paramref name="head"/>, in layout order.</summary>
        private List<LoweredBlock<TBlock>> SubtreeMembers(TBlock head) =>
            loops.Dominators.GetDominatedBlocks(head)
                .Select(block => _memberByBlock.GetValueOrDefault(block))
                .OfType<LoweredBlock<TBlock>>()
                .OrderBy(member => positionByBlock[member.Block])
                .ToList();

        /// <summary>
        /// The statements of the lowered dominator subtree of <paramref name="head"/>, walked without materializing
        /// its members when they are not needed: the head's branch decides whether this is the usual walk
        /// (materialized) or one where the subtree stays a lazy region.
        /// </summary>
        private IEnumerable<AstSubcall> WalkSubtreeIt(LoweredBlock<TBlock> head,
            NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            if (loops.LoopWithHeader(head.Block) is NaturalLoop<TBlock> loop && loop != enclosing
                && IsStructurable(loop, _memberByBlock, entryTargets)) {
                yield return AstTrampoline.Call(WalkIt(SubtreeMembers(head.Block), enclosing, result));
                yield break;
            }
            Region<TBlock> region = Region<TBlock>.FullSubtree(head.Block,
                subtreeMemberCount[head.Block], loops.Dominators, _memberByBlock, blockByEntry, positionByBlock);
            PlacedBlocks<TBlock> placed = new(loops.Dominators, subtreeMemberCount);
            placed.Add(head.Block);
            AstResult<BlockNode> withIf = new();
            yield return AstTrampoline.Call(BranchStatementIt(head, region, placed, enclosing, withIf));
            if (withIf.Value is null) {
                yield return AstTrampoline.Call(WalkIt(SubtreeMembers(head.Block), enclosing, result));
                yield break;
            }
            List<IVisitableAstNode> statements = [withIf.Value];
            List<LoweredBlock<TBlock>> remaining = [];
            (int start, int end) = loops.Dominators.SubtreeRange(head.Block);
            IReadOnlyList<TBlock> preorder = loops.Dominators.PreorderBlocks;
            for (int index = start + 1; index < end; index++) {
                if (placed.TryGetSubtreeRangeEnd(index, out int rangeEnd)) {
                    index = rangeEnd - 1;
                    continue;
                }
                TBlock block = preorder[index];
                if (!placed.Contains(block) && _memberByBlock.TryGetValue(block, out LoweredBlock<TBlock>? member)) {
                    remaining.Add(member);
                }
            }
            remaining.Sort((left, right) => positionByBlock[left.Block].CompareTo(positionByBlock[right.Block]));
            yield return AstTrampoline.Call(WalkBlocksIt(remaining, region, placed, statements, enclosing));
            result.Value = Sequence(statements);
        }

        private IEnumerable<AstSubcall> WalkBlocksIt(List<LoweredBlock<TBlock>> order, Region<TBlock> region,
            PlacedBlocks<TBlock> placed, List<IVisitableAstNode> statements, NaturalLoop<TBlock>? enclosing) {
            for (int index = 0; index < order.Count; index++) {
                if (placed.Count == region.MemberCount) {
                    break;
                }
                LoweredBlock<TBlock> block = order[index];
                if (placed.Contains(block.Block)) {
                    continue;
                }
                if (loops.LoopWithHeader(block.Block) is NaturalLoop<TBlock> loop && loop != enclosing
                    && IsStructurable(loop, _memberByBlock, entryTargets)) {
                    List<LoweredBlock<TBlock>> loopMembers = loop.Blocks.Select(region.MemberOf).OfType<LoweredBlock<TBlock>>()
                        .OrderBy(region.LayoutIndex).ToList();
                    if (!ReferenceEquals(loopMembers[0].Block, loop.Header)) {
                        throw new InvalidOperationException(
                            $"Generated method {methodName}: loop header {loopMembers[0].Entry} is not the first block of its loop in the layout.");
                    }
                    AstResult<IVisitableAstNode> preTest = new();
                    yield return AstTrampoline.Call(PreTestLoopStatementIt(loop, loopMembers, preTest));
                    DoWhileShape? shape = preTest.Value is null ? DoWhileShapeOf(loop, loopMembers) : null;
                    if (preTest.Value is null) {
                        yield return AstTrampoline.Call(LoopStatementIt(loop, loopMembers, shape, preTest));
                    }
                    statements.Add(preTest.Value!);
                    placed.UnionWith(loopMembers.Select(member => member.Block));
                    if (placeLoopExits) {
                        MoveExitAfterLoop(order, index, loop, loopMembers, placed, shape);
                    }
                    continue;
                }
                placed.Add(block.Block);
                AstResult<BlockNode> withIf = new();
                yield return AstTrampoline.Call(BranchStatementIt(block, region, placed, enclosing, withIf));
                if (withIf.Value is not null) {
                    statements.Add(withIf.Value);
                    continue;
                }
                AstResult<BlockNode> withSwitch = new();
                yield return AstTrampoline.Call(DispatchStatementIt(block, region, placed, enclosing, withSwitch));
                if (withSwitch.Value is not null) {
                    statements.Add(withSwitch.Value);
                    continue;
                }
                statements.Add(block.Body);
            }
        }

        private void Record(GotoNode? jump, GotoKeepReason? reason) {
            if (jump is not null && reason is not null) {
                refusals[jump] = reason.Value;
            }
        }

        /// <summary>
        /// The <c>while (c)</c> loop of a pre-tested header, or <c>null</c>: applies when the header block's body has
        /// no statement other than the final branch (labels aside), the branch has one arm that is a <c>goto</c>
        /// inside the loop and one arm outside it. The body is the walk of the other loop members and the outside
        /// arm's <c>goto</c> follows the loop.
        /// </summary>
        private IEnumerable<AstSubcall> PreTestLoopStatementIt(NaturalLoop<TBlock> loop,
            IReadOnlyList<LoweredBlock<TBlock>> members, AstResult<IVisitableAstNode> result) {
            LoweredBlock<TBlock> header = members[0];
            if (FinalStatement<IfElseNode>(header.Body) is not IfElseNode branch
                || !HeadIsTransparent(header)
                || !ReadsOnlyMachineState(branch.Condition)) {
                yield break;
            }
            GotoNode? trueGoto = OnlyGoto(branch.TrueCase);
            GotoNode? falseGoto = OnlyGoto(branch.FalseCase);
            if (trueGoto is null || falseGoto is null) {
                yield break;
            }
            bool trueInside = members.Any(member => Equals(member.Entry, trueGoto.Target));
            bool falseInside = members.Any(member => Equals(member.Entry, falseGoto.Target));
            ValueNode condition;
            IGotoTarget exit;
            if (trueInside && !falseInside) {
                condition = branch.Condition;
                exit = falseGoto.Target;
            } else if (falseInside && !trueInside) {
                condition = NegatedCondition(branch.Condition);
                exit = trueGoto.Target;
            } else {
                yield break;
            }
            AstResult<BlockNode> body = new();
            yield return AstTrampoline.Call(WalkIt([.. members.Skip(1)], loop, body));
            result.Value = Sequence(new PreTestLoopNode(condition, body.Value!, header.Entry), new GotoNode(exit));
        }

        /// <summary>Every head statement of <paramref name="header"/> is a label or empty: the body holds only the final branch.</summary>
        private static bool HeadIsTransparent(LoweredBlock<TBlock> header) =>
            HeadBefore(header.Body).All(statement => statement is LabelNode || IsEmpty(statement)
                || (statement is InstructionBoundaryNode boundary && IsEmpty(boundary.Body)));

        private IEnumerable<AstSubcall> LoopStatementIt(NaturalLoop<TBlock> loop,
            IReadOnlyList<LoweredBlock<TBlock>> members, DoWhileShape? shape, AstResult<IVisitableAstNode> result) {
            AstResult<BlockNode> body = new();
            if (shape is not null) {
                List<LoweredBlock<TBlock>> bodyMembers = [.. members.Take(members.Count - 1), members[^1] with { Body = shape.LatchBody }];
                yield return AstTrampoline.Call(WalkIt(bodyMembers, loop, body));
                result.Value = Sequence(new DoWhileNode(body.Value!, shape.Condition), shape.Exit);
                yield break;
            }
            yield return AstTrampoline.Call(WalkIt(members, loop, body));
            result.Value = new InfiniteLoopNode(body.Value!);
        }

        /// <summary>
        /// Moves the exit of <paramref name="loop"/> that the loop's <c>goto</c>s target the most right after the loop
        /// in <paramref name="order"/> (in front of the first unplaced block after <paramref name="index"/>), so the
        /// <c>goto</c>s to it print as <c>break</c>s. The exit of a <c>do</c> loop is the target of the goto after its
        /// latch; for a <c>while (true)</c> loop it is the block outside the loop the most <c>goto</c>s of the loop
        /// members' bodies target, ties broken on the earliest in <paramref name="order"/>. The exit is moved only
        /// when it is an unplaced block of <paramref name="order"/> other than the next block and strictly more
        /// targeted than that next block (the <c>do</c> loop's exit goto counts as one). Every other unplaced block
        /// the exit dominates moves with it, keeping their relative order, so its fall-throughs stay fall-throughs.
        /// It may make the method worse (the moved exit can break fall-throughs of the blocks it jumps over), so
        /// <see cref="MethodBodyEmitter"/> structures the method both with and without the move and keeps the result with
        /// fewer <c>goto</c>s.
        /// </summary>
        private void MoveExitAfterLoop(List<LoweredBlock<TBlock>> order, int index, NaturalLoop<TBlock> loop,
            IReadOnlyList<LoweredBlock<TBlock>> loopMembers, PlacedBlocks<TBlock> placed, DoWhileShape? shape) {
            Dictionary<IGotoTarget, int> exitCountByTarget = loopMembers.SelectMany(member => Gotos(member.Body))
                .CountBy(jump => jump.Target).ToDictionary(pair => pair.Key, pair => pair.Value);
            IGotoTarget? preferredTarget = null;
            if (shape is not null && shape.Exit is GotoNode exitGoto) {
                preferredTarget = exitGoto.Target;
            } else {
                int most = 0;
                foreach (LoweredBlock<TBlock> candidate in order) {
                    if (placed.Contains(candidate.Block) || loop.Contains(candidate.Block)) {
                        continue;
                    }
                    int count = exitCountByTarget.GetValueOrDefault(candidate.Entry);
                    if (count > most) {
                        most = count;
                        preferredTarget = candidate.Entry;
                    }
                }
            }
            if (preferredTarget is null) {
                return;
            }
            int exitIndex = order.FindIndex(held => Equals(held.Entry, preferredTarget));
            if (exitIndex < 0 || placed.Contains(order[exitIndex].Block)) {
                return;
            }
            int nextIndex = -1;
            for (int position = index + 1; position < order.Count; position++) {
                if (!placed.Contains(order[position].Block)) {
                    nextIndex = position;
                    break;
                }
            }
            if (nextIndex < 0 || exitIndex == nextIndex) {
                return;
            }
            int preferredCount = exitCountByTarget.GetValueOrDefault(preferredTarget);
            int nextCount = exitCountByTarget.GetValueOrDefault(order[nextIndex].Entry);
            if (preferredCount <= nextCount) {
                return;
            }
            LoweredBlock<TBlock> exit = order[exitIndex];
            List<LoweredBlock<TBlock>> run = order.Skip(index + 1).Where(held => !placed.Contains(held.Block)
                && (ReferenceEquals(held, exit) || loops.Dominators.Dominates(exit.Block, held.Block))).ToList();
            foreach (LoweredBlock<TBlock> held in run) {
                order.Remove(held);
            }
            order.InsertRange(nextIndex, run);
            MovedLoopExit = true;
        }

        private DoWhileShape? DoWhileShapeOf(NaturalLoop<TBlock> loop, IReadOnlyList<LoweredBlock<TBlock>> members) {
            LoweredBlock<TBlock> latch = members[^1];
            if (loop.Latches is not [TBlock onlyLatch] || !ReferenceEquals(onlyLatch, latch.Block)
                || loops.InnermostLoopOf(latch.Block) != loop) {
                return null;
            }
            if (FinalStatement<IfElseNode>(latch.Body) is not IfElseNode branch
                || !ReadsOnlyMachineState(branch.Condition)) {
                return null;
            }
            IGotoTarget header = members[0].Entry;
            ValueNode condition;
            IVisitableAstNode exitArm;
            if (IsOnlyGotoTo(branch.TrueCase, header)) {
                condition = branch.Condition;
                exitArm = branch.FalseCase;
            } else if (IsOnlyGotoTo(branch.FalseCase, header)) {
                condition = NegatedCondition(branch.Condition);
                exitArm = branch.TrueCase;
            } else {
                return null;
            }
            if (ExitStatement(exitArm, header) is not IVisitableAstNode exit) {
                return null;
            }
            BlockNode latchBody = Sequence(HeadBefore(latch.Body));
            return new DoWhileShape(latchBody, condition, exit);
        }

        /// <summary>
        /// <paramref name="block"/> with the arms of its final forward branch nested: in an <c>if</c> when exactly one arm
        /// comes back to the other target, in an <c>if</c> / <c>else</c> when neither does and both arms meet at one join
        /// block, or in an <c>if</c> without <c>else</c> when an arm diverges (the arm whose head is later in layout order
        /// when both do); <c>null</c> otherwise. Marks the nested blocks as placed.
        /// </summary>
        private IEnumerable<AstSubcall> BranchStatementIt(LoweredBlock<TBlock> block, Region<TBlock> region,
            PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            if (FinalStatement<IfElseNode>(block.Body) is not IfElseNode branch) {
                yield break;
            }
            GotoNode? trueGoto = OnlyGoto(branch.TrueCase);
            GotoNode? falseGoto = OnlyGoto(branch.FalseCase);
            if (!ConditionNestable(block, branch.Condition)) {
                Record(trueGoto, GotoKeepReason.ConditionReadsLocals);
                Record(falseGoto, GotoKeepReason.ConditionReadsLocals);
                yield break;
            }
            if (trueGoto is null || falseGoto is null) {
                Record(trueGoto, GotoKeepReason.OtherArmNotGoto);
                Record(falseGoto, GotoKeepReason.OtherArmNotGoto);
                yield break;
            }
            IGotoTarget whenTrue = trueGoto.Target;
            IGotoTarget whenFalse = falseGoto.Target;
            if (Equals(whenTrue, whenFalse)) {
                Record(trueGoto, GotoKeepReason.NotNextInLayout);
                Record(falseGoto, GotoKeepReason.NotNextInLayout);
                yield break;
            }
            (Arm<TBlock>? trueArm, GotoKeepReason? trueFailure) = ArmBlocksWithReason(whenTrue, region, placed, enclosing);
            (Arm<TBlock>? falseArm, GotoKeepReason? falseFailure) = ArmBlocksWithReason(whenFalse, region, placed, enclosing);
            bool trueComesBack = trueArm is not null && ComesBackTo(trueArm, whenFalse, region, placed);
            bool falseComesBack = falseArm is not null && ComesBackTo(falseArm, whenTrue, region, placed);
            yield return AstTrampoline.Call(OneArmComesBackIt(block, branch, trueArm, falseArm,
                trueComesBack, falseComesBack, whenTrue, whenFalse, placed, enclosing, result));
            if (result.Value is not null) {
                yield break;
            }
            yield return AstTrampoline.Call(IfElseWithJoinIt(block, branch, trueArm, falseArm,
                trueComesBack, falseComesBack, region, placed, enclosing, result));
            if (result.Value is not null) {
                yield break;
            }
            yield return AstTrampoline.Call(GuardClauseIt(block, branch, trueArm, falseArm, region,
                whenTrue, whenFalse, placed, enclosing, result));
            if (result.Value is not null) {
                yield break;
            }
            RecordArmFailure(trueGoto, trueArm, trueFailure, falseArm);
            RecordArmFailure(falseGoto, falseArm, falseFailure, trueArm);
        }

        /// <summary>
        /// Whether the condition can move out of its instruction boundary into an <c>if</c>: it reads only machine
        /// state, or (with <c>allowLocalsDeclaredInBlock</c>) every local variable it reads is declared by a
        /// statement of <paramref name="block"/> so the nested <c>if</c> stays in the declarations' scope.
        /// </summary>
        private bool ConditionNestable(LoweredBlock<TBlock> block, ValueNode condition) =>
            ReadsOnlyMachineState(condition)
            || (allowLocalsDeclaredInBlock && VariablesDeclaredInScope(block, condition));

        /// <summary>Every local variable <paramref name="condition"/> reads is declared inside
        /// <paramref name="block"/>'s body or a block's that dominates it.</summary>
        private bool VariablesDeclaredInScope(LoweredBlock<TBlock> block, ValueNode condition) {
            HashSet<string> names = [];
            if (!CollectVariableReferences(condition, names)) {
                return false;
            }
            HashSet<string> declared = [];
            CollectDeclarations(block.Body, declared);
            if (_declaredInDominatingBlocks.TryGetValue(block.Block, out HashSet<string>? dominating)) {
                declared.UnionWith(dominating);
            }
            return names.All(declared.Contains);
        }

        /// <summary>The local variable names each block sees declared by the lowered blocks that
        /// strictly dominate it: one walk down the dominator tree accumulating the declarations of
        /// the ancestors, so every block gets exactly its dominators' names.</summary>
        internal static Dictionary<TBlock, HashSet<string>> DeclaredInDominatingBlocks(
            IReadOnlyList<LoweredBlock<TBlock>> blocks, DominatorTree<TBlock> dominators) {
            Dictionary<TBlock, HashSet<string>> declarationsByBlock = [];
            Dictionary<TBlock, List<TBlock>> childrenByBlock = [];
            List<TBlock> roots = [];
            foreach (LoweredBlock<TBlock> block in blocks) {
                HashSet<string> names = [];
                CollectDeclarations(block.Body, names);
                declarationsByBlock[block.Block] = names;
                TBlock? immediate = dominators.GetImmediateDominator(block.Block);
                if (immediate is null) {
                    roots.Add(block.Block);
                } else {
                    if (!childrenByBlock.TryGetValue(immediate, out List<TBlock>? children)) {
                        childrenByBlock[immediate] = children = [];
                    }
                    children.Add(block.Block);
                }
            }
            Dictionary<TBlock, HashSet<string>> declared = [];
            Stack<(TBlock Block, HashSet<string> Inherited)> work = new();
            foreach (TBlock root in roots) {
                work.Push((root, []));
            }
            while (work.Count > 0) {
                (TBlock block, HashSet<string> inherited) = work.Pop();
                declared[block] = inherited;
                if (!childrenByBlock.TryGetValue(block, out List<TBlock>? children)) {
                    continue;
                }
                HashSet<string> forChildren = [.. inherited];
                forChildren.UnionWith(declarationsByBlock[block]);
                foreach (TBlock child in children) {
                    work.Push((child, forChildren));
                }
            }
            return declared;
        }

        /// <summary>Adds the local variable names <paramref name="node"/> reads to <paramref name="names"/>;
        /// <c>false</c> when a node shape the collector does not know occurs (the caller then refuses, staying
        /// correct).</summary>
        private static bool CollectVariableReferences(IVisitableAstNode node, HashSet<string> names) =>
            IrAstChildren.Descendants(node).All(descendant => descendant switch {
                MethodScopedVariableReferenceNode => true,
                VariableReferenceNode reference => AddName(names, reference),
                BinaryOperationNode or UnaryOperationNode or ExplicitCastNode or TypeConversionNode
                    or SegmentedPointerNode or AbsolutePointerNode or SegmentedAddressNode
                    or InstructionFieldNode or MethodCallNode or MethodCallValueNode
                    or AluOperationNode or OpaqueNode
                    or ConstantNode or RegisterNode or SegmentRegisterNode or CpuFlagNode
                    or FlagRegisterNode or InstructionPointerNode or LoadOffsetParameterNode
                    or SegmentVariableNode or OffsetLiteralNode or SignatureFieldNode
                    or NearAddressNode => true,
                _ => false
            });

        private static bool AddName(HashSet<string> names, VariableReferenceNode reference) {
            names.Add(reference.VariableName);
            return true;
        }

        /// <summary>The names declared by <see cref="VariableDeclarationNode"/>s inside <paramref name="node"/>.</summary>
        private static void CollectDeclarations(IVisitableAstNode node, HashSet<string> declared) {
            Stack<IVisitableAstNode> pending = new();
            pending.Push(node);
            while (pending.Count > 0) {
                IVisitableAstNode current = pending.Pop();
                if (current is VariableDeclarationNode declaration) {
                    declared.Add(declaration.VariableName);
                }
                foreach (IVisitableAstNode child in ChildStatements(current)) {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// One arm comes back to the other target: it nests in an <c>if</c> and the other target follows the <c>if</c>.
        /// </summary>
        private IEnumerable<AstSubcall> OneArmComesBackIt(LoweredBlock<TBlock> block, IfElseNode branch,
            Arm<TBlock>? trueArm, Arm<TBlock>? falseArm, bool trueComesBack, bool falseComesBack,
            IGotoTarget whenTrue, IGotoTarget whenFalse, PlacedBlocks<TBlock> placed,
            NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            if (trueArm is not null && trueComesBack && !falseComesBack) {
                yield return AstTrampoline.Call(NestedIt(block, branch.Condition, trueArm, null, whenFalse, placed, enclosing, result));
                yield break;
            }
            if (falseArm is not null && falseComesBack && !trueComesBack) {
                yield return AstTrampoline.Call(NestedIt(block, NegatedCondition(branch.Condition), falseArm, null, whenTrue, placed, enclosing, result));
            }
        }

        /// <summary>Two arms that neither come back nor diverge and meet at one join nest in an <c>if</c> / <c>else</c>.</summary>
        private IEnumerable<AstSubcall> IfElseWithJoinIt(LoweredBlock<TBlock> block, IfElseNode branch,
            Arm<TBlock>? trueArm, Arm<TBlock>? falseArm, bool trueComesBack, bool falseComesBack,
            Region<TBlock> region, PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing,
            AstResult<BlockNode> result) {
            if (trueComesBack || falseComesBack || trueArm is null || falseArm is null
                || CommonJoin(trueArm, falseArm, region, placed) is not TBlock join) {
                yield break;
            }
            yield return AstTrampoline.Call(NestedIt(block, branch.Condition, trueArm, falseArm,
                _memberByBlock[join].Entry, placed, enclosing, result));
        }

        /// <summary>
        /// An arm that never leaves (a guard clause) nests in an <c>if</c> without <c>else</c>; when both arms diverge,
        /// the arm whose head is later in layout order.
        /// </summary>
        private IEnumerable<AstSubcall> GuardClauseIt(LoweredBlock<TBlock> block, IfElseNode branch,
            Arm<TBlock>? trueArm, Arm<TBlock>? falseArm, Region<TBlock> region, IGotoTarget whenTrue,
            IGotoTarget whenFalse, PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing,
            AstResult<BlockNode> result) {
            if (trueArm is not null && Diverges(trueArm) && NestDivergingArm(region, trueArm, falseArm)) {
                yield return AstTrampoline.Call(NestedIt(block, branch.Condition, trueArm, null, whenFalse, placed, enclosing, result));
                yield break;
            }
            if (falseArm is not null && Diverges(falseArm)) {
                yield return AstTrampoline.Call(NestedIt(block, NegatedCondition(branch.Condition), falseArm, null, whenTrue, placed, enclosing, result));
            }
        }

        private bool Diverges(Arm<TBlock> arm) => Exits(arm.Members).Count == 0;

        private bool NestDivergingArm(Region<TBlock> region, Arm<TBlock> arm, Arm<TBlock>? otherArm) {
            if (otherArm is null || !Diverges(otherArm)) {
                return true;
            }
            return region.LayoutIndex(arm.Head) >= region.LayoutIndex(otherArm.Head);
        }

        /// <summary>Records why <paramref name="jump"/>'s arm could not nest: <paramref name="failure"/> when it has no
        /// arm, <see cref="GotoKeepReason.ArmNeverComesBack"/> when the other arm does not exist, otherwise
        /// <see cref="GotoKeepReason.NoSingleJoin"/>.</summary>
        private void RecordArmFailure(GotoNode jump, Arm<TBlock>? arm, GotoKeepReason? failure, Arm<TBlock>? otherArm) {
            if (arm is null) {
                Record(jump, failure);
                return;
            }
            if (otherArm is null) {
                Record(jump, GotoKeepReason.ArmNeverComesBack);
                return;
            }
            Record(jump, GotoKeepReason.NoSingleJoin);
        }

        /// <summary>
        /// The statements of <paramref name="block"/> before its final branch, then <c>if (condition) { then arm }
        /// else { else arm }</c> (no <c>else</c> when <paramref name="elseArm"/> is <c>null</c>), then
        /// <c>goto after</c>. Marks the arms' blocks as placed.
        /// </summary>
        private IEnumerable<AstSubcall> NestedIt(LoweredBlock<TBlock> block, ValueNode condition,
            Arm<TBlock> thenArm, Arm<TBlock>? elseArm, IGotoTarget after, PlacedBlocks<TBlock> placed,
            NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            PlaceArm(thenArm, placed);
            BlockNode elseBody = Empty();
            if (elseArm is not null) {
                PlaceArm(elseArm, placed);
                AstResult<BlockNode> elseResult = new();
                yield return AstTrampoline.Call(WalkArmIt(elseArm, enclosing, elseResult));
                elseBody = elseResult.Value!;
            }
            AstResult<BlockNode> thenResult = new();
            yield return AstTrampoline.Call(WalkArmIt(thenArm, enclosing, thenResult));
            IfElseNode nested = new(condition, thenResult.Value!, elseBody);
            result.Value = Sequence(HeadBefore(block.Body).Append(nested).Append(new GotoNode(after)));
        }

        /// <summary>Places <paramref name="arm"/>'s blocks: its whole subtree when it is lazy, else each member.</summary>
        private void PlaceArm(Arm<TBlock> arm, PlacedBlocks<TBlock> placed) {
            if (arm.IsFullSubtree) {
                placed.AddSubtree(arm.Head.Block);
            } else {
                placed.UnionWith(arm.Members.Select(member => member.Block));
            }
        }

        /// <summary>Walks <paramref name="arm"/> into <paramref name="result"/>: the subtree path when it
        /// is one, the list path otherwise.</summary>
        private IEnumerable<AstSubcall> WalkArmIt(Arm<TBlock> arm, NaturalLoop<TBlock>? enclosing,
            AstResult<BlockNode> result) {
            if (arm.IsFullSubtree || ArmIsLoweredSubtree(arm)) {
                yield return AstTrampoline.Call(WalkSubtreeIt(arm.Head, enclosing, result));
                yield break;
            }
            yield return AstTrampoline.Call(WalkIt(arm.Members, enclosing, result));
        }

        /// <summary>Whether <paramref name="arm"/> holds exactly the lowered dominator subtree of its head: same
        /// count, and every member is the method's canonical member of its block (a do-while latch copy is not).</summary>
        private bool ArmIsLoweredSubtree(Arm<TBlock> arm) =>
            arm.Count == subtreeMemberCount.GetValueOrDefault(arm.Head.Block, -1)
            && arm.Members.All(member => ReferenceEquals(member, _memberByBlock.GetValueOrDefault(member.Block)));

        /// <summary>
        /// The blocks of the arm that starts at <paramref name="head"/>, in layout order, or <c>null</c> when that arm
        /// cannot be nested: its head is placed, targeted by more than one <c>goto</c>, a loop header or holds a
        /// method entry, or the arm holds a latch of <paramref name="enclosing"/>.
        /// </summary>
        private Arm<TBlock>? ArmBlocks(IGotoTarget head, Region<TBlock> region, PlacedBlocks<TBlock> placed,
            NaturalLoop<TBlock>? enclosing) =>
            ArmBlocksWithReason(head, region, placed, enclosing).Arm;

        /// <summary>The arm of <see cref="ArmBlocks"/>, or the reason it cannot be nested. In a lazy subtree region
        /// whose only placed block is the head, the arm is the full lowered dominator subtree of its head and stays
        /// lazy: a member of the subtree is a member of the region, and the head is the only placed block.</summary>
        private (Arm<TBlock>? Arm, GotoKeepReason? Reason) ArmBlocksWithReason(IGotoTarget head,
            Region<TBlock> region, PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing) {
            LoweredBlock<TBlock>? headBlock = region.MemberWithEntry(head);
            if (headBlock is null) {
                return (null, GotoKeepReason.ArmOutsideRegion);
            }
            if (placed.Contains(headBlock.Block)) {
                return (null, GotoKeepReason.ArmHeadPlaced);
            }
            if (gotoCountByTarget.GetValueOrDefault(head) != 1) {
                return (null, GotoKeepReason.SharedTarget);
            }
            if (loops.LoopWithHeader(headBlock.Block) is not null) {
                return (null, GotoKeepReason.ArmHeadIsLoopHeader);
            }
            if (headBlock.HoldsEntry) {
                return (null, GotoKeepReason.TargetIsEntry);
            }
            if (region.SubtreeHead is not null && placed.Count == 1
                && subtreeMemberCount.TryGetValue(headBlock.Block, out int memberCount)) {
                if (enclosing is not null && enclosing.Latches.Any(latch =>
                        loops.Dominators.Dominates(headBlock.Block, latch) && _memberByBlock.ContainsKey(latch))) {
                    return (null, GotoKeepReason.ArmHoldsLatch);
                }
                if (subtreeFirstPosition[headBlock.Block] != positionByBlock[headBlock.Block]) {
                    throw new InvalidOperationException(
                        $"Generated method {methodName}: arm head {head} is not the first block of its arm in the layout.");
                }
                return (new Arm<TBlock>(headBlock, memberCount, null,
                    () => SubtreeMembers(headBlock.Block)), null);
            }
            List<LoweredBlock<TBlock>> arm = loops.Dominators.GetDominatedBlocks(headBlock.Block)
                .Select(region.MemberOf).OfType<LoweredBlock<TBlock>>()
                .Where(member => !placed.Contains(member.Block))
                .OrderBy(region.LayoutIndex).ToList();
            if (enclosing is not null && arm.Any(member => enclosing.Latches.Contains(member.Block))) {
                return (null, GotoKeepReason.ArmHoldsLatch);
            }
            if (!ReferenceEquals(arm[0], headBlock)) {
                throw new InvalidOperationException(
                    $"Generated method {methodName}: arm head {head} is not the first block of its arm in the layout.");
            }
            return (new Arm<TBlock>(headBlock, arm.Count, arm, () => arm), null);
        }

        /// <summary>Whether <paramref name="block"/> is a member of the arm rooted at <paramref name="head"/>:
        /// a block of <paramref name="head"/>'s dominator subtree that is a member of <paramref name="region"/>
        /// and not yet placed.</summary>
        private bool InArm(TBlock block, TBlock head, Region<TBlock> region, PlacedBlocks<TBlock> placed) =>
            loops.Dominators.Dominates(head, block) && region.MemberOf(block) is not null && !placed.Contains(block);

        /// <summary>Whether <paramref name="target"/> has a graph predecessor that is a member of the arm rooted
        /// at <paramref name="head"/>.</summary>
        private bool HasPredecessorInArm(TBlock target, TBlock head, Region<TBlock> region, PlacedBlocks<TBlock> placed) {
            if (!reachablePredecessors.TryGetValue(target, out (int Preorder, TBlock Block)[]? predecessors)) {
                return false;
            }
            (int start, int end) = loops.Dominators.SubtreeRange(head);
            if (end <= start) {
                return false;
            }
            int low = 0;
            int high = predecessors.Length;
            while (low < high) {
                int mid = low + (high - low) / 2;
                if (predecessors[mid].Preorder < start) {
                    low = mid + 1;
                } else {
                    high = mid;
                }
            }
            for (int position = low; position < predecessors.Length && predecessors[position].Preorder < end; position++) {
                if (InArm(predecessors[position].Block, head, region, placed)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a member of <paramref name="arm"/> has an edge to the block whose entry is
        /// <paramref name="target"/>.</summary>
        private bool ComesBackTo(Arm<TBlock> arm, IGotoTarget target, Region<TBlock> region,
            PlacedBlocks<TBlock> placed) =>
            blockByEntry.TryGetValue(target, out TBlock? targetBlock)
            && HasPredecessorInArm(targetBlock, arm.Head.Block, region, placed);

        /// <summary>The only block that both arms exit to, or <c>null</c> when there is none or more than one.</summary>
        private TBlock? CommonJoin(Arm<TBlock> first, Arm<TBlock> second,
            Region<TBlock> region, PlacedBlocks<TBlock> placed) {
            (Arm<TBlock> small, Arm<TBlock> large) =
                first.Count <= second.Count ? (first, second) : (second, first);
            List<TBlock> common = Exits(small.Members)
                .Where(exit => !InArm(exit, large.Head.Block, region, placed)
                    && HasPredecessorInArm(exit, large.Head.Block, region, placed))
                .ToList();
            if (common is [TBlock join]) {
                return join;
            }
            return null;
        }

        private HashSet<TBlock> Exits(IReadOnlyList<LoweredBlock<TBlock>> arm) {
            HashSet<TBlock> inside = arm.Select(member => member.Block).ToHashSet();
            return arm.SelectMany(member => loops.Graph.GetSuccessors(member.Block))
                .Where(successor => !inside.Contains(successor))
                .ToHashSet();
        }

        /// <summary>
        /// <paramref name="block"/> with the arms of its final near jump dispatch nested in their <c>case</c> sections, or
        /// <c>null</c> when fewer than two sections take their arm. An arm head entered only through inside edges
        /// (<see cref="LoweredBlock{TBlock}.EnteredOnlyFromInside"/>) nests even on its own: it is a call dispatch
        /// arm inside one instruction. A section takes the arm of its <c>goto</c> target when
        /// that arm can be nested and never leaves, or leaves only to the join: the only exit shared by every candidate
        /// arm that has exactly one exit. Marks the nested blocks as placed.
        /// </summary>
        private IEnumerable<AstSubcall> DispatchStatementIt(LoweredBlock<TBlock> block, Region<TBlock> region,
            PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing, AstResult<BlockNode> result) {
            if (FinalStatement<SwitchNode>(block.Body) is not SwitchNode dispatch) {
                yield break;
            }
            if (!ConditionNestable(block, dispatch.Value)) {
                RecordFlatSections(dispatch.Sections, []);
                yield break;
            }
            Dictionary<uint, Arm<TBlock>> armByLabel = dispatch.Sections
                .SelectMany(section => SectionArm(section, region, placed, enclosing)).ToDictionary();
            List<TBlock> onlyExits = armByLabel.Values.Select(arm => Exits(arm.Members)).Where(exits => exits.Count == 1)
                .Select(exits => exits.Single()).Distinct().ToList();
            TBlock? join = null;
            if (onlyExits is [TBlock single]) {
                join = single;
            }
            List<uint> caseLabels = armByLabel.Where(pair => IsCaseArm(pair.Value, join)).Select(pair => pair.Key).ToList();
            List<uint> insideLabels = caseLabels.Where(label => armByLabel[label].Head.EnteredOnlyFromInside).ToList();
            Arm<TBlock>? defaultArm = null;
            if (OnlyGotoTarget(dispatch.Default) is IGotoTarget defaultTarget
                && ArmBlocks(defaultTarget, region, placed, enclosing) is Arm<TBlock> arm
                && arm.Head.EnteredOnlyFromInside && IsCaseArm(arm, join)) {
                defaultArm = arm;
            }
            if (caseLabels.Count < 2 && insideLabels.Count == 0 && defaultArm is null) {
                RecordFlatSections(dispatch.Sections, []);
                yield break;
            }
            List<uint> nestedLabels = caseLabels.Count >= 2 ? caseLabels : insideLabels;
            foreach (uint label in nestedLabels) {
                PlaceArm(armByLabel[label], placed);
            }
            List<SwitchSection> sections = [];
            foreach (SwitchSection section in dispatch.Sections) {
                if (nestedLabels.Contains(section.Label)) {
                    AstResult<BlockNode> armBody = new();
                    yield return AstTrampoline.Call(WalkArmIt(armByLabel[section.Label], enclosing, armBody));
                    sections.Add(section with { Body = armBody.Value! });
                } else {
                    sections.Add(section);
                }
            }
            BlockNode defaultBody = dispatch.Default;
            if (defaultArm is not null) {
                PlaceArm(defaultArm, placed);
                AstResult<BlockNode> armBody = new();
                yield return AstTrampoline.Call(WalkArmIt(defaultArm, enclosing, armBody));
                defaultBody = armBody.Value!;
            }
            RecordFlatSections(sections, nestedLabels);
            List<IVisitableAstNode> statements = [.. HeadBefore(block.Body),
                dispatch with { Sections = sections, Default = defaultBody }];
            if (join is not null && _memberByBlock.TryGetValue(join, out LoweredBlock<TBlock>? joinMember)) {
                statements.Add(new GotoNode(joinMember.Entry));
            }
            result.Value = Sequence(statements);
        }

        /// <summary>Records <see cref="GotoKeepReason.SwitchSection"/> for each section of <paramref name="sections"/>
        /// whose label is not in <paramref name="nestedLabels"/> and whose body is a single <c>goto</c>.</summary>
        private void RecordFlatSections(IReadOnlyList<SwitchSection> sections, IReadOnlyCollection<uint> nestedLabels) {
            foreach (SwitchSection section in sections.Where(section => !nestedLabels.Contains(section.Label))) {
                Record(OnlyGoto(section.Body), GotoKeepReason.SwitchSection);
            }
        }

        /// <summary>The label and arm of <paramref name="section"/> when its body is a single <c>goto</c> to an arm
        /// that can be nested (see <see cref="ArmBlocks"/>), otherwise nothing.</summary>
        private IEnumerable<KeyValuePair<uint, Arm<TBlock>>> SectionArm(SwitchSection section,
            Region<TBlock> region, PlacedBlocks<TBlock> placed, NaturalLoop<TBlock>? enclosing) {
            if (OnlyGotoTarget(section.Body) is IGotoTarget target
                && ArmBlocks(target, region, placed, enclosing) is Arm<TBlock> arm) {
                yield return new(section.Label, arm);
            }
        }

        private bool IsCaseArm(Arm<TBlock> arm, TBlock? join) {
            HashSet<TBlock> exits = Exits(arm.Members);
            return exits.Count == 0 || (join is not null && exits.Count == 1 && exits.Contains(join));
        }
    }
}
