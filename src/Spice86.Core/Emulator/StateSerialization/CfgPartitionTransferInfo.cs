namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// Serialized inter-partition CFG transfer classified by the partitioner.
/// </summary>
internal sealed record CfgPartitionTransferInfo {
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("fromPartition")] public required int FromPartition { get; init; }
    [JsonPropertyName("toPartition")] public required int ToPartition { get; init; }
    [JsonPropertyName("fromBlock")] public required int FromBlock { get; init; }
    [JsonPropertyName("toBlock")] public required int ToBlock { get; init; }
    [JsonPropertyName("from")] public required string From { get; init; }
    [JsonPropertyName("target")] public required string Target { get; init; }
    [JsonPropertyName("callContinuationBlock")] public int? CallContinuationBlock { get; init; }
    [JsonPropertyName("callContinuationAddress")] public string? CallContinuationAddress { get; init; }
}
