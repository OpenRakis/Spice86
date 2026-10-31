namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>
/// Dominator relationships between blocks for a selected graph slice, indexed by a dominator-tree
/// pre-order walk: a block dominates exactly the contiguous pre-order range of its subtree.
/// </summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
internal sealed class DominatorTree<TBlock> where TBlock : class {
    private readonly TBlock[] _preorder;
    private readonly Dictionary<TBlock, int> _preorderIndex;
    private readonly int[] _subtreeSize;

    public DominatorTree(
        IReadOnlyList<TBlock> blocks,
        IReadOnlyDictionary<TBlock, TBlock?> immediateDominatorByBlock) {
        Blocks = blocks;
        ImmediateDominatorByBlock = immediateDominatorByBlock;

        Dictionary<TBlock, List<TBlock>> childrenByBlock = blocks.ToDictionary(block => block, _ => new List<TBlock>());
        List<TBlock> roots = [];
        foreach (TBlock block in blocks) {
            if (immediateDominatorByBlock.TryGetValue(block, out TBlock? parent) && parent is not null) {
                childrenByBlock[parent].Add(block);
            } else {
                roots.Add(block);
            }
        }

        List<TBlock> preorder = [];
        _preorderIndex = new Dictionary<TBlock, int>();
        List<(TBlock Node, int NextChild)> stack = [.. Enumerable.Reverse(roots).Select(root => (root, 0))];
        while (stack.Count > 0) {
            (TBlock node, int nextChild) = stack[^1];
            if (nextChild == 0) {
                _preorderIndex.Add(node, preorder.Count);
                preorder.Add(node);
            }
            if (nextChild < childrenByBlock[node].Count) {
                stack[^1] = (node, nextChild + 1);
                stack.Add((childrenByBlock[node][nextChild], 0));
            } else {
                stack.RemoveAt(stack.Count - 1);
            }
        }
        _preorder = [.. preorder];

        _subtreeSize = new int[_preorder.Length];
        Array.Fill(_subtreeSize, 1);
        for (int i = _preorder.Length - 1; i > 0; i--) {
            TBlock? parent = immediateDominatorByBlock[_preorder[i]];
            if (parent is not null && _preorderIndex.TryGetValue(parent, out int parentIndex)) {
                _subtreeSize[parentIndex] += _subtreeSize[i];
            }
        }
    }

    public IReadOnlyList<TBlock> Blocks { get; }
    public IReadOnlyDictionary<TBlock, TBlock?> ImmediateDominatorByBlock { get; }

    public bool Dominates(TBlock dominator, TBlock block) {
        if (!_preorderIndex.TryGetValue(dominator, out int dominatorIndex)
            || !_preorderIndex.TryGetValue(block, out int blockIndex)) {
            return false;
        }
        return dominatorIndex <= blockIndex && blockIndex < dominatorIndex + _subtreeSize[dominatorIndex];
    }

    /// <summary>The reachable blocks in dominator-tree pre-order.</summary>
    internal IReadOnlyList<TBlock> PreorderBlocks => _preorder;

    /// <summary>The dominator-tree pre-order index of <paramref name="block"/>, or -1 when it is not reachable.</summary>
    internal int PreorderIndex(TBlock block) =>
        _preorderIndex.TryGetValue(block, out int index) ? index : -1;

    /// <summary>The contiguous pre-order index range of the subtree rooted at <paramref name="block"/>,
    /// end exclusive; <c>(0, 0)</c> when the block is not reachable.</summary>
    internal (int Start, int End) SubtreeRange(TBlock block) {
        if (!_preorderIndex.TryGetValue(block, out int index)) {
            return (0, 0);
        }
        return (index, index + _subtreeSize[index]);
    }

    /// <summary>The block and every block it dominates, in dominator-tree pre-order; empty when the block is not reachable.</summary>
    public IReadOnlyList<TBlock> GetDominatedBlocks(TBlock block) {
        if (!_preorderIndex.TryGetValue(block, out int index)) {
            return [];
        }
        return new ArraySegment<TBlock>(_preorder, index, _subtreeSize[index]);
    }

    public TBlock? GetImmediateDominator(TBlock block) {
        if (ImmediateDominatorByBlock.TryGetValue(block, out TBlock? immediateDominator)) {
            return immediateDominator;
        }
        return null;
    }
}
