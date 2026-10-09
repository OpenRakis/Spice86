namespace Spice86.Tests;

using FluentAssertions;

using Xunit;

/// <summary>
/// Every CPU test bin runs emulated and as generated code, which shows as a listing golden and at least
/// one generated-code golden.
/// </summary>
public sealed class AsmFixtureGoldenCoverageTest {
    // Engine-only fixtures: no oracle, so no generated-code run.
    private static readonly string[] EngineOnlyBins = ["speculative_seeded_timer_sweep"];

    [Fact]
    public void EveryCpuTestBinHasAnEmulatedAndAGeneratedCodeGolden() {
        // Arrange
        string[] generatedGoldens = Directory.GetFiles(Path.Join("Resources", "cpuTests", "res", "GeneratedCode"), "*.cs.txt")
            .Select(Path.GetFileName).OfType<string>().ToArray();
        string[] binNames = Directory.GetFiles(Path.Join("Resources", "cpuTests"), "*.bin")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(binName => !EngineOnlyBins.Contains(binName)).ToArray();

        // Act
        List<string> missing = new();
        foreach (string binName in binNames) {
            if (!File.Exists(Path.Join("Resources", "cpuTests", "res", "DumpedListing", binName + ".txt"))) {
                missing.Add($"{binName}: no MachineTest listing golden");
            }
            if (!generatedGoldens.Any(golden => golden == binName + ".cs.txt" || golden.StartsWith(binName + ".", StringComparison.Ordinal))) {
                missing.Add($"{binName}: no generated-code golden");
            }
        }

        // Assert
        missing.Should().BeEmpty("every ASM fixture runs in MachineTest and as generated code (see AGENTS.md)");
    }
}
