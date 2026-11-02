namespace Spice86.Tests.Dos;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Runs every TSR scenario of <see cref="AsmScenarioCatalog"/> in interpreted mode. Setup and
/// oracle live once in the catalogue, so the generated entry point checks the very same expectations.
/// The scenarios cover the seven exit-code programs, the parent-PSP allocation regression and the batch
/// that continues after the resident program terminated.</summary>
public class TsrIntegrationTests {
    /// <summary>Identities of every TSR scenario.</summary>
    public static IEnumerable<object[]> TsrScenarioIds =>
        AsmScenarioCatalog.TsrScenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(TsrScenarioIds))]
    public void TerminateAndStayResidentScenarioProducesItsExpectedResult(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
