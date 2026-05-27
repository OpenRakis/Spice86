namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// Serialized ownership metadata for one recovered CFG partition.
/// </summary>
internal sealed record CfgPartitionInfo {
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("blocks")] public required int[] Blocks { get; init; }
    [JsonPropertyName("entries")] public required CfgPartitionEntryInfo[] Entries { get; init; }
}
