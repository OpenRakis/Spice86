namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// On-disk partition overlay derived from the CFG block graph: the recovered partitions and the
/// inter-partition transfers between them. Dumped to its own file (separate from <see cref="CfgBlocksDump"/>)
/// so a partitioning failure cannot prevent the block graph from being written. Block ids referenced here
/// index into the blocks dumped alongside it.
/// </summary>
internal sealed record CfgPartitionsDump {
    [JsonPropertyName("partitions")] public required CfgPartitionInfo[] Partitions { get; init; }
    [JsonPropertyName("transfers")] public required CfgPartitionTransferInfo[] Transfers { get; init; }
    // True when the graph was truncated (a node limit was hit): partitioning requires the full graph, so no
    // partitions are emitted. Null (omitted) for a full graph.
    [JsonPropertyName("partitioningRequiresFullGraph")] public bool? PartitioningRequiresFullGraph { get; init; }
}
