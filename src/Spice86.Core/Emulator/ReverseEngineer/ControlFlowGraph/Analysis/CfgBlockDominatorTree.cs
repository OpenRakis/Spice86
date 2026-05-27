namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>
/// Dominator relationships between CFG blocks for a selected graph slice.
/// </summary>
internal sealed class CfgBlockDominatorTree {
    private readonly HashSet<CfgBlock> _blocks;
    private readonly IReadOnlyDictionary<CfgBlock, HashSet<CfgBlock>> _dominatorsByBlock;

    public CfgBlockDominatorTree(
        IReadOnlyList<CfgBlock> blocks,
        IReadOnlyDictionary<CfgBlock, HashSet<CfgBlock>> dominatorsByBlock,
        IReadOnlyDictionary<CfgBlock, CfgBlock?> immediateDominatorByBlock) {
        Blocks = blocks;
        _blocks = blocks.ToHashSet();
        _dominatorsByBlock = dominatorsByBlock;
        ImmediateDominatorByBlock = immediateDominatorByBlock;
    }

    public IReadOnlyList<CfgBlock> Blocks { get; }
    public IReadOnlyDictionary<CfgBlock, CfgBlock?> ImmediateDominatorByBlock { get; }

    public bool Dominates(CfgBlock dominator, CfgBlock block) {
        if (dominator.Equals(block)) {
            return _blocks.Contains(block);
        }
        return _dominatorsByBlock.TryGetValue(block, out HashSet<CfgBlock>? dominators)
            && dominators.Contains(dominator);
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