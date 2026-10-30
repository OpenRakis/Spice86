namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>
/// One natural loop of a <see cref="BlockGraph{TBlock}"/>: its header, its blocks and the blocks that jump back to
/// the header. Reachable blocks outside the loop only enter it through the header.
/// </summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
internal sealed class NaturalLoop<TBlock> where TBlock : class {
    private readonly HashSet<TBlock> _blockSet;

    internal NaturalLoop(TBlock header, IReadOnlyList<TBlock> blocks, IReadOnlyList<TBlock> latches) {
        Header = header;
        Blocks = blocks;
        Latches = latches;
        _blockSet = blocks.ToHashSet();
    }

    /// <summary>The block every back edge of the loop jumps to.</summary>
    public TBlock Header { get; }

    /// <summary>Every block of the loop, header included, in <see cref="BlockGraph{TBlock}.Blocks"/> order.</summary>
    public IReadOnlyList<TBlock> Blocks { get; }

    /// <summary>The sources of the back edges, in <see cref="BlockGraph{TBlock}.Blocks"/> order.</summary>
    public IReadOnlyList<TBlock> Latches { get; }

    /// <summary>Whether <paramref name="block"/> is a block of the loop.</summary>
    public bool Contains(TBlock block) => _blockSet.Contains(block);
}
