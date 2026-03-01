namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.JsonDto;

using System.Text.Json;

/// <summary>
/// Represents an x86 CPU test case with initial and final states
/// From https://github.com/SingleStepTests/
/// </summary>
public class CpuTest {
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] Bytes { get; set; } = [];
    public CpuState Initial { get; set; } = new();
    public CpuState Final { get; set; } = new();
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Deserialize from JSON using DTO approach
    /// </summary>
    public static CpuTest FromJson(string json) {
        var dto = JsonSerializer.Deserialize<CpuTestDto>(json)
            ?? throw new JsonException("Failed to deserialize CpuTest");
        return CpuTestConverter.ToModel(dto);
    }
}

// ============================================================================
// JSON DTOs - Simple structures that map directly to JSON
// ============================================================================