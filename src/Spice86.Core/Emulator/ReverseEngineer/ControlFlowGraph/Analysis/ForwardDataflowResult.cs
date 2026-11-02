namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

/// <summary>The fixed point of a <see cref="ForwardDataflowSolver"/> run: the value at the entry and at the exit of every block.</summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
/// <typeparam name="T">The dataflow value type.</typeparam>
/// <param name="In">The value at the start of each block.</param>
/// <param name="Out">The value after <c>transfer</c> of each block, for each block.</param>
internal sealed record ForwardDataflowResult<TBlock, T>(IReadOnlyDictionary<TBlock, T> In,
    IReadOnlyDictionary<TBlock, T> Out) where TBlock : class;
