namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Runs every catalogue scenario as generated code, one case per generated-source golden: a fresh
/// discovery run whose oracle passes before anything is generated and whose finished machine is then
/// compared with the listing and CFG goldens, generation, compilation with the metrics gate, and every
/// variant of the case executed with the compiled override against its own oracle, ending with the
/// generated-source golden. The cases come from <see cref="GeneratedScenarioCatalog"/>, so a scenario
/// added to the catalogue is a generated case here without editing this class.
/// </summary>
public sealed class GeneratedCodeScenarioTest {
    /// <summary>Generated-source key of every catalogue case.</summary>
    public static IEnumerable<object[]> CaseKeys => GeneratedScenarioCatalog.CaseKeys;

    /// <summary>Discovers, compiles and re-runs one generated case, checking the oracle of every variant
    /// execution and the generated-source golden they share.</summary>
    /// <param name="generatedSourceKey">Generated-source key of the case to run.</param>
    [Theory]
    [MemberData(nameof(CaseKeys))]
    public void CatalogueCaseRunsAsGeneratedCode(string generatedSourceKey) {
        // Arrange
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case(generatedSourceKey);

        // Act & Assert: discovery, listing/CFG goldens, compilation, metrics, every guarded execution
        // with its own oracle and the generated-source golden
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            scenarioCase.DiscoveryScenario, scenarioCase.ExecutionScenarios);
    }
}
