namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The block-level view of one block set: blocks, entries, successors and predecessors, and fallthrough
/// successors, generic over the block type. Feeds <see cref="DominatorTreeBuilder"/>.
/// </summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
internal sealed class BlockGraph<TBlock> where TBlock : class {
    private readonly Dictionary<TBlock, TBlock?> _fallthroughSuccessorByBlock;
    private readonly Func<TBlock, string> _describe;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockGraph{TBlock}"/> class.
    /// </summary>
    /// <param name="blocks">All blocks, kept in the given order.</param>
    /// <param name="entryBlocks">Entry blocks, index 0 = primary.</param>
    /// <param name="successorsByBlock">Successor relation.</param>
    /// <param name="fallthroughSuccessorByBlock">Fallthrough relation.</param>
    /// <param name="stableOrder">The order consumers sort blocks in.</param>
    /// <param name="describe">Renders a block for exception messages.</param>
    /// <exception cref="ArgumentException">Validation failed.</exception>
    internal BlockGraph(
        IReadOnlyList<TBlock> blocks,
        IReadOnlyList<TBlock> entryBlocks,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock,
        IReadOnlyDictionary<TBlock, TBlock?> fallthroughSuccessorByBlock,
        IComparer<TBlock> stableOrder,
        Func<TBlock, string> describe) {
        _describe = describe;
        HashSet<TBlock> blockSet = new HashSet<TBlock>();
        foreach (TBlock block in blocks) {
            if (!blockSet.Add(block)) {
                throw new ArgumentException($"Duplicate block: {describe(block)}");
            }
        }

        if (entryBlocks.Count == 0) {
            throw new ArgumentException("Entry blocks cannot be empty");
        }
        foreach (TBlock entryBlock in entryBlocks) {
            if (!blockSet.Contains(entryBlock)) {
                throw new ArgumentException($"Entry block not in blocks: {describe(entryBlock)}");
            }
        }

        // Report violations in a deterministic order: stray keys in stable order, then the value
        // checks in Blocks order, so the failure does not depend on dictionary enumeration.
        TBlock? straySuccessorKey = successorsByBlock.Keys
            .Where(key => !blockSet.Contains(key))
            .OrderBy(key => key, stableOrder)
            .FirstOrDefault();
        if (straySuccessorKey != null) {
            throw new ArgumentException($"Successor key not in blocks: {describe(straySuccessorKey)}");
        }
        TBlock? strayFallthroughKey = fallthroughSuccessorByBlock.Keys
            .Where(key => !blockSet.Contains(key))
            .OrderBy(key => key, stableOrder)
            .FirstOrDefault();
        if (strayFallthroughKey != null) {
            throw new ArgumentException($"Fallthrough key not in blocks: {describe(strayFallthroughKey)}");
        }

        foreach (TBlock block in blocks) {
            HashSet<TBlock> seen = new HashSet<TBlock>();
            if (successorsByBlock.TryGetValue(block, out IReadOnlyList<TBlock>? successors)) {
                foreach (TBlock succ in successors) {
                    if (!blockSet.Contains(succ)) {
                        throw new ArgumentException($"Successor not in blocks: {describe(succ)}");
                    }
                    if (!seen.Add(succ)) {
                        throw new ArgumentException($"Duplicate successor: {describe(succ)}");
                    }
                }
            }
            if (fallthroughSuccessorByBlock.TryGetValue(block, out TBlock? fallthrough)
                && fallthrough != null && !seen.Contains(fallthrough)) {
                throw new ArgumentException($"Fallthrough not in successors: {describe(fallthrough)}");
            }
        }

        Blocks = blocks.ToList();
        EntryBlocks = entryBlocks.ToList();
        PrimaryEntryBlock = EntryBlocks[0];
        StableOrder = stableOrder;

        Dictionary<TBlock, IReadOnlyList<TBlock>> succDict = new Dictionary<TBlock, IReadOnlyList<TBlock>>();
        foreach (TBlock block in Blocks) {
            succDict[block] = successorsByBlock.TryGetValue(block, out IReadOnlyList<TBlock>? list)
                ? list.ToList() : new List<TBlock>();
        }
        SuccessorsByBlock = succDict;

        Dictionary<TBlock, List<TBlock>> predDict = new Dictionary<TBlock, List<TBlock>>();
        foreach (TBlock block in Blocks) {
            predDict[block] = new List<TBlock>();
        }
        foreach (TBlock block in Blocks) {
            foreach (TBlock succ in SuccessorsByBlock[block]) {
                predDict[succ].Add(block);
            }
        }
        PredecessorsByBlock = predDict.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<TBlock>)kvp.Value);

        _fallthroughSuccessorByBlock = new Dictionary<TBlock, TBlock?>();
        foreach (TBlock block in Blocks) {
            _fallthroughSuccessorByBlock[block] = fallthroughSuccessorByBlock.TryGetValue(block, out TBlock? fb) ? fb : null;
        }
    }

    /// <summary>
    /// All blocks, in the order given to the constructor.
    /// </summary>
    public IReadOnlyList<TBlock> Blocks { get; }

    /// <summary>
    /// Entry blocks; index 0 is the primary entry, the others follow in the given order.
    /// </summary>
    public IReadOnlyList<TBlock> EntryBlocks { get; }

    /// <summary>
    /// Primary entry block (EntryBlocks[0]).
    /// </summary>
    public TBlock PrimaryEntryBlock { get; }

    /// <summary>
    /// The order consumers sort blocks in.
    /// </summary>
    public IComparer<TBlock> StableOrder { get; }

    /// <summary>
    /// Successor relation, one key per block; each list keeps the order given to the constructor.
    /// </summary>
    public IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> SuccessorsByBlock { get; }

    /// <summary>
    /// Predecessor relation, one key per block, in Blocks order.
    /// </summary>
    public IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> PredecessorsByBlock { get; }

    /// <summary>
    /// Renders a block for exception messages.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The description.</returns>
    public string Describe(TBlock block) => _describe(block);

    /// <summary>
    /// Gets the successors of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The successors, in the order of <see cref="SuccessorsByBlock"/>.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public IReadOnlyList<TBlock> GetSuccessors(TBlock block) => SuccessorsByBlock[block];

    /// <summary>
    /// Gets the predecessors of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The predecessors in Blocks order.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public IReadOnlyList<TBlock> GetPredecessors(TBlock block) => PredecessorsByBlock[block];

    /// <summary>
    /// Gets the fallthrough successor of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The fallthrough successor or null.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public TBlock? GetFallthroughSuccessor(TBlock block) => _fallthroughSuccessorByBlock[block];
}
