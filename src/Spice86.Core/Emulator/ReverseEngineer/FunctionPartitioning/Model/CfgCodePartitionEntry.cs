namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Evidence that identifies an entry point into a partition.
/// </summary>
internal sealed class CfgCodePartitionEntry {
    public required ICfgNode Node { get; init; }
    public required CfgCodePartitionEntryKind Kind { get; init; }

    public CfgBlock Block => Node.ContainingBlock
        ?? throw new InvalidOperationException("Partition entry node has no containing block.");

    public SegmentedAddress Address => Node.Address;
}