namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Text.Json.Serialization;

/// <summary>
/// A single CFG node. <c>type</c> is <c>"instruction"</c> or <c>"selector"</c>. <c>bytes</c> is the
/// per-node sigHex (the byte image with <c>__</c> for modified-immediate fields read from memory at
/// runtime); absent for selectors. <c>maxSucc</c> is the runtime-mutated successor cap; absent for
/// selectors and for instructions whose cap is unbounded (<c>null</c>).
/// </summary>
internal sealed record CfgReloadNodeInfo {
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("addr")] public required string Addr { get; init; }
    [JsonPropertyName("bytes")] public string? Bytes { get; init; }
    [JsonPropertyName("maxSucc")] public int? MaxSucc { get; init; }
}
