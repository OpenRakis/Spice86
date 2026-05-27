namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.Mcp.Response;

using System.Text.Json.Serialization;

/// <summary>
/// Universal model for the CFG graph with execution-context metadata. Used for both the
/// on-disk dump and the MCP wire format.
/// </summary>
internal sealed record CfgCpuGraph {
    [JsonPropertyName("currentContextDepth")] public required int CurrentContextDepth { get; init; }
    [JsonPropertyName("currentContextEntryPoint")] public required string CurrentContextEntryPoint { get; init; }
    [JsonPropertyName("totalEntryPoints")] public required int TotalEntryPoints { get; init; }
    [JsonPropertyName("entryPointAddresses")] public required string[] EntryPointAddresses { get; init; }
    [JsonPropertyName("lastExecutedAddress")] public string? LastExecutedAddress { get; init; }
    [JsonPropertyName("lastExecutedBlockId")] public int? LastExecutedBlockId { get; init; }
    [JsonPropertyName("blocks")] public required CfgBlockInfo[] Blocks { get; init; }
    [JsonPropertyName("partitions")] public CfgPartitionInfo[]? Partitions { get; init; }
    [JsonPropertyName("transfers")] public CfgPartitionTransferInfo[]? Transfers { get; init; }
    [JsonPropertyName("partitioningRequiresFullGraph")] public bool? PartitioningRequiresFullGraph { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
}
