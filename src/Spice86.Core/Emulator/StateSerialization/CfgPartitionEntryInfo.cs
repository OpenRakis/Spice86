namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// Serialized evidence that identifies a partition entry point.
/// </summary>
internal sealed record CfgPartitionEntryInfo {
    [JsonPropertyName("block")] public required int Block { get; init; }
    [JsonPropertyName("address")] public required string Address { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
}
