namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>
/// One natural loop of a <see cref="PartitionBlockGraph"/>: its header, its blocks and the blocks that jump back to
/// the header. Reachable blocks outside the loop only enter it through the header.
/// </summary>
internal sealed class NaturalLoop {
    private readonly HashSet<CfgBlock> _blockSet;

    internal NaturalLoop(CfgBlock header, IReadOnlyList<CfgBlock> blocks, IReadOnlyList<CfgBlock> latches) {
        Header = header;
        Blocks = blocks;
        Latches = latches;
        _blockSet = blocks.ToHashSet();
    }

    /// <summary>The block every back edge of the loop jumps to.</summary>
    public CfgBlock Header { get; }

    /// <summary>Every block of the loop, header included, in <see cref="PartitionBlockGraph.Blocks"/> order.</summary>
    public IReadOnlyList<CfgBlock> Blocks { get; }

    /// <summary>The sources of the back edges, in <see cref="PartitionBlockGraph.Blocks"/> order.</summary>
    public IReadOnlyList<CfgBlock> Latches { get; }

    /// <summary>Whether <paramref name="block"/> is a block of the loop.</summary>
    public bool Contains(CfgBlock block) => _blockSet.Contains(block);
}