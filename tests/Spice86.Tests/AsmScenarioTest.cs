namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Runs every catalogue scenario, one case per generated-source golden: each non-discovery variant
/// first runs interpreted against its oracle, engine assertions and machine goldens, then the
/// discovery run records the program, its C# is generated, compiled with the metrics gate, and every
/// variant executes with the compiled override against its own oracle, ending with the
/// generated-source golden. The cases come from <see cref="GeneratedScenarioCatalog"/> and run in
/// parallel under the assembly-wide parallel mode.
/// </summary>
public sealed class AsmScenarioTest {
    /// <summary>Generated-source key of every catalogue case.</summary>
    public static IEnumerable<object[]> CaseKeys => GeneratedScenarioCatalog.CaseKeys;

    /// <summary>Runs one generated case: interpreted checks, discovery, compilation, every guarded
    /// execution with its own oracle and the generated-source golden they share.</summary>
    /// <param name="generatedSourceKey">Generated-source key of the case to run.</param>
    [Theory]
    [MemberData(nameof(CaseKeys))]
    public void CatalogueCase(string generatedSourceKey) {
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case(generatedSourceKey);
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            scenarioCase.DiscoveryScenario, scenarioCase.ExecutionScenarios);
    }
}
