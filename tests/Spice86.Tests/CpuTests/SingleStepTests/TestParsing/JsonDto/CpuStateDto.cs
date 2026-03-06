namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.JsonDto;

using System.Text.Json.Serialization;

/// <summary>
/// JSON DTO for CpuState deserialization
/// </summary>
internal class CpuStateDto {
    [JsonPropertyName("regs")] public CpuRegistersDto Registers { get; set; } = new();
    [JsonPropertyName("ram")] public uint[][]? Ram { get; set; }
    [JsonPropertyName("queue")] public object[]? Queue { get; set; } // Ignored, but present in JSON
}