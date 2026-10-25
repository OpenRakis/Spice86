namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>
/// Dominator relationships between CFG blocks for a selected graph slice, indexed by a dominator-tree
/// pre-order walk: a block dominates exactly the contiguous pre-order range of its subtree.
/// </summary>
internal sealed class CfgBlockDominatorTree {
    private readonly CfgBlock[] _preorder;
    private readonly Dictionary<CfgBlock, int> _preorderIndex;
    private readonly int[] _subtreeSize;

    public CfgBlockDominatorTree(
        IReadOnlyList<CfgBlock> blocks,
        IReadOnlyDictionary<CfgBlock, CfgBlock?> immediateDominatorByBlock) {
        Blocks = blocks;
        ImmediateDominatorByBlock = immediateDominatorByBlock;

        Dictionary<CfgBlock, List<CfgBlock>> childrenByBlock = blocks.ToDictionary(block => block, _ => new List<CfgBlock>());
        List<CfgBlock> roots = [];
        foreach (CfgBlock block in blocks) {
            if (immediateDominatorByBlock.TryGetValue(block, out CfgBlock? parent) && parent is not null) {
                childrenByBlock[parent].Add(block);
            } else {
                roots.Add(block);
            }
        }

        List<CfgBlock> preorder = [];
        _preorderIndex = new Dictionary<CfgBlock, int>();
        List<(CfgBlock Node, int NextChild)> stack = [.. Enumerable.Reverse(roots).Select(root => (root, 0))];
        while (stack.Count > 0) {
            (CfgBlock node, int nextChild) = stack[^1];
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
            CfgBlock? parent = immediateDominatorByBlock[_preorder[i]];
            if (parent is not null && _preorderIndex.TryGetValue(parent, out int parentIndex)) {
                _subtreeSize[parentIndex] += _subtreeSize[i];
            }
        }
    }

    public IReadOnlyList<CfgBlock> Blocks { get; }
    public IReadOnlyDictionary<CfgBlock, CfgBlock?> ImmediateDominatorByBlock { get; }

    public bool Dominates(CfgBlock dominator, CfgBlock block) {
        if (!_preorderIndex.TryGetValue(dominator, out int dominatorIndex)
            || !_preorderIndex.TryGetValue(block, out int blockIndex)) {
            return false;
        }
        return dominatorIndex <= blockIndex && blockIndex < dominatorIndex + _subtreeSize[dominatorIndex];
    }

    /// <summary>The block and every block it dominates, in dominator-tree pre-order; empty when the block is not reachable.</summary>
    public IReadOnlyList<CfgBlock> GetDominatedBlocks(CfgBlock block) {
        if (!_preorderIndex.TryGetValue(block, out int index)) {
            return [];
        }
        return new ArraySegment<CfgBlock>(_preorder, index, _subtreeSize[index]);
    }

    public CfgBlock? GetImmediateDominator(CfgBlock block) {
        if (ImmediateDominatorByBlock.TryGetValue(block, out CfgBlock? immediateDominator)) {
            return immediateDominator;
        }
        return null;
    }

    public IReadOnlyList<CfgBlock> GetStrictlyDominatedBlocks(CfgBlock block) => Blocks
        .Where(candidate => !candidate.Equals(block) && Dominates(block, candidate))
        .OrderBy(candidate => candidate.Entry.Address)
        .ThenBy(candidate => candidate.Id)
        .ToArray();
}
