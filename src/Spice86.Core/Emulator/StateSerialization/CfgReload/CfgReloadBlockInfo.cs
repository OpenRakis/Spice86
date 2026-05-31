namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Text.Json.Serialization;

/// <summary>
/// A block, as an ordered list of node ids (entry first, terminator last) plus its discovery state.
/// <c>discoveryComplete</c> cannot be re-derived from the node/edge set, so it is authoritative.
/// </summary>
internal sealed record CfgReloadBlockInfo {
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("nodes")] public required int[] Nodes { get; init; }
    [JsonPropertyName("discoveryComplete")] public required bool DiscoveryComplete { get; init; }
}
