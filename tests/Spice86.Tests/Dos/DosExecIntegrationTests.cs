namespace Spice86.Tests.Dos;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Runs every EXEC scenario of <see cref="AsmScenarioCatalog"/> in interpreted mode: the master
/// program with its overlay and the FCB parent with its child. Setup and oracle live once in the
/// catalogue, so the generated entry point checks the very same expectations.</summary>
public class DosExecIntegrationTests {
    /// <summary>Identities of every EXEC scenario.</summary>
    public static IEnumerable<object[]> ExecScenarioIds =>
        AsmScenarioCatalog.ExecScenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(ExecScenarioIds))]
    public void ExecScenarioProducesItsExpectedResult(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
