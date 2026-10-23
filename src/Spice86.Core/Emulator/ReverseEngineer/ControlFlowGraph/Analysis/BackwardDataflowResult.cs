namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The fixed point of a <see cref="BackwardDataflowSolver"/> run: the value at the entry and at the exit of every block.</summary>
/// <param name="LiveIn">The value at the start of each block.</param>
/// <param name="LiveOut">The join of the successors' <paramref name="LiveIn"/> values, for each block.</param>
internal sealed record BackwardDataflowResult<T>(IReadOnlyDictionary<CfgBlock, T> LiveIn, IReadOnlyDictionary<CfgBlock, T> LiveOut);
