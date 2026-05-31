namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Text.Json.Serialization;

/// <summary>
/// A typed instruction-level edge. <c>type</c> is the name of an
/// <see cref="Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.InstructionSuccessorType"/>.
/// </summary>
internal sealed record CfgReloadEdgeInfo {
    [JsonPropertyName("from")] public required int From { get; init; }
    [JsonPropertyName("to")] public required int To { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
}
