namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;

using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// A single CFG snapshot produced by <see cref="CfgCpuSnapshotBuilder"/>: the exported block graph plus the
/// partition overlay derived from it. <see cref="PartitionedProgram"/> is <c>null</c> when the graph was
/// truncated (a node limit was hit), since partitioning requires the full graph. Both downstream consumers
/// (JSON dump and C# generation) read from the same snapshot, so the graph is exported and partitioned once.
/// </summary>
internal sealed class CfgCpuSnapshot {
    public required CfgExecutionContextGraph Exported { get; init; }
    public CfgPartitionedProgram? PartitionedProgram { get; init; }
}
