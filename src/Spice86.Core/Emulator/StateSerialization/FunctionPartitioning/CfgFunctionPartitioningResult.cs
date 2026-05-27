namespace Spice86.Core.Emulator.StateSerialization.FunctionPartitioning;

using Spice86.Core.Emulator.StateSerialization;

/// <summary>
/// Partitions and inter-partition transfers derived from a full CFG block graph.
/// </summary>
internal sealed record CfgFunctionPartitioningResult {
    public required CfgPartitionInfo[] Partitions { get; init; }
    public required CfgPartitionTransferInfo[] Transfers { get; init; }
}
