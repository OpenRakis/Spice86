namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Inter-partition control-flow transfer over existing CFG nodes.
/// </summary>
internal sealed class CfgCodePartitionTransfer {
    public required CfgCodePartition FromPartition { get; init; }
    public required CfgCodePartition ToPartition { get; init; }
    public required ICfgNode FromNode { get; init; }
    public required ICfgNode TargetNode { get; init; }
    public required CfgCodePartitionTransferKind Kind { get; init; }

    public ICfgNode? CallContinuationNode { get; init; }

    public CfgBlock FromBlock => FromNode.ContainingBlock
        ?? throw new InvalidOperationException("Transfer source node has no containing block.");

    public CfgBlock ToBlock => TargetNode.ContainingBlock
        ?? throw new InvalidOperationException("Transfer target node has no containing block.");

    public SegmentedAddress From => FromNode.Address;
    public SegmentedAddress Target => TargetNode.Address;
    public SegmentedAddress? CallContinuationAddress => CallContinuationNode?.Address;
}