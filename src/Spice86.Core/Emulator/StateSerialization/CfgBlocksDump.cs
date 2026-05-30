namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// On-disk blocks-only view of the CFG graph with execution-context metadata. Written to its own file so it is
/// always produced, even when partitioning fails: the block graph is what reload consumes and what an engineer
/// inspects to diagnose a partitioning invariant violation. The partition overlay is dumped separately in
/// <see cref="CfgPartitionsDump"/>.
/// </summary>
internal sealed record CfgBlocksDump {
    [JsonPropertyName("currentContextDepth")] public required int CurrentContextDepth { get; init; }
    [JsonPropertyName("currentContextEntryPoint")] public required string CurrentContextEntryPoint { get; init; }
    [JsonPropertyName("totalEntryPoints")] public required int TotalEntryPoints { get; init; }
    [JsonPropertyName("entryPointAddresses")] public required string[] EntryPointAddresses { get; init; }
    [JsonPropertyName("lastExecutedAddress")] public string? LastExecutedAddress { get; init; }
    [JsonPropertyName("lastExecutedBlockId")] public int? LastExecutedBlockId { get; init; }
    [JsonPropertyName("blocks")] public required CfgBlockInfo[] Blocks { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
}
