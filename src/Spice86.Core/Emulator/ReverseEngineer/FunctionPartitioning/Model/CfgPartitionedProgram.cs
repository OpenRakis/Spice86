namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Immutable partition overlay produced for a CFG snapshot.
/// </summary>
internal sealed class CfgPartitionedProgram {
    public required IReadOnlyList<CfgCodePartition> Partitions { get; init; }
    public required IReadOnlyList<CfgCodePartitionTransfer> Transfers { get; init; }

    /// <summary>The machine state the snapshot was partitioned with: hardware interrupt setup,
    /// loaded images, changed bytes, memory snapshot, registered I/O ports and entry
    /// point.</summary>
    public required GeneratorInputs Inputs { get; init; }
}
