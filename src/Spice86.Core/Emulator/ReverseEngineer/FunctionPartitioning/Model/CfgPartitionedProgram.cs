namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Immutable partition overlay produced for a CFG snapshot.
/// </summary>
internal sealed class CfgPartitionedProgram {
    public required IReadOnlyList<CfgCodePartition> Partitions { get; init; }
    public required IReadOnlyList<CfgCodePartitionTransfer> Transfers { get; init; }
}