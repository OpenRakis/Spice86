namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Text.Json.Serialization;

/// <summary>
/// Machine-oriented serialization of the CFG CPU instruction graph, used to reload the graph into
/// live emulator state on a subsequent run. This is intentionally a separate artifact from the
/// LLM-token-optimized <see cref="CfgCpuGraph"/> block dump: it carries the data required for an
/// exact reconstruction (per-node bytes, full typed edge tables, ordered block membership) that
/// would otherwise bloat the human/LLM dump.
/// </summary>
internal sealed record CfgReloadDump {
    /// <summary>
    /// Value the live <c>SequentialIdAllocator</c> is seeded to after reload (<c>maxId + 1</c>), so
    /// resumed execution never hands out an id that collides with a reloaded node.
    /// </summary>
    [JsonPropertyName("idAllocatorNext")] public required int IdAllocatorNext { get; init; }

    /// <summary>Entry-point addresses (<c>segment:offset</c>) resolved to nodes after reconstruction.</summary>
    [JsonPropertyName("entryPoints")] public required string[] EntryPoints { get; init; }

    /// <summary>All reachable nodes (instructions and selectors), with ids preserved verbatim.</summary>
    [JsonPropertyName("nodes")] public required CfgReloadNodeInfo[] Nodes { get; init; }

    /// <summary>All typed instruction-level edges between nodes.</summary>
    [JsonPropertyName("edges")] public required CfgReloadEdgeInfo[] Edges { get; init; }

    /// <summary>Ordered block membership; authoritative on import (blocks are rebuilt from this).</summary>
    [JsonPropertyName("blocks")] public required CfgReloadBlockInfo[] Blocks { get; init; }
}
