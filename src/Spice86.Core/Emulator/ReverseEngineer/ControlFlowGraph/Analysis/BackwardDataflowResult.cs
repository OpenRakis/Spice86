namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

/// <summary>The fixed point of a <see cref="BackwardDataflowSolver"/> run: the value at the entry and at the exit of every block.</summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
/// <typeparam name="T">The dataflow value type.</typeparam>
/// <param name="LiveIn">The value at the start of each block.</param>
/// <param name="LiveOut">The join of the successors' <paramref name="LiveIn"/> values, for each block.</param>
internal sealed record BackwardDataflowResult<TBlock, T>(IReadOnlyDictionary<TBlock, T> LiveIn,
    IReadOnlyDictionary<TBlock, T> LiveOut) where TBlock : class;
