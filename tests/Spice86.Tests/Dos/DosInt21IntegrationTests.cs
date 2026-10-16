namespace Spice86.Tests.Dos;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h functionality that run DOS COM resources through the emulation
/// stack. Every scenario comes from <see cref="AsmScenarioCatalog.Int21Scenarios"/>: setup and oracle
/// live once in the shared catalogue, so the generated entry point checks the very same expectations,
/// and each run prepares its own host files, disk images and input from fresh state.
/// </summary>
public class DosInt21IntegrationTests {
    /// <summary>Identities of every INT 21h scenario.</summary>
    public static IEnumerable<object[]> Int21ScenarioIds =>
        AsmScenarioCatalog.Int21Scenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(Int21ScenarioIds))]
    public void Int21ScenarioProducesItsExpectedResult(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
