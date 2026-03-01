namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.JsonDto;

using System.Text.Json.Serialization;

/// <summary>
/// JSON DTO for CpuTest deserialization
/// </summary>
internal class CpuTestDto {
    [JsonPropertyName("idx")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("bytes")] public uint[] Bytes { get; set; } = Array.Empty<uint>();
    [JsonPropertyName("initial")] public CpuStateDto Initial { get; set; } = new();
    [JsonPropertyName("final")] public CpuStateDto Final { get; set; } = new();
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
}