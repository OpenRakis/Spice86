namespace Spice86.Core.Emulator.StateSerialization;

using System.Text.Json.Serialization;

/// <summary>
/// Compact json serialization of a single <c>CfgBlock</c>.
/// </summary>
internal sealed record CfgBlockInfo {
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("entry")] public required string Entry { get; init; }
    // Inverted from IsLive: null (omitted) means live, true means dead. Most blocks are live,
    // so this avoids emitting the field for the common case and saves LLM context tokens.
    [JsonPropertyName("dead")] public bool? Dead { get; init; }
    // Inverted from IsDiscoveryComplete: null (omitted) means complete, true means incomplete.
    // Most blocks are fully discovered, so this avoids emitting the field for the common case.
    [JsonPropertyName("incomplete")] public bool? Incomplete { get; init; }
    [JsonPropertyName("term")] public required string Term { get; init; }
    [JsonPropertyName("pred")] public required int[] Pred { get; init; }
    [JsonPropertyName("succ")] public required int[] Succ { get; init; }
    [JsonPropertyName("asm")] public required string[] Asm { get; init; }
}
