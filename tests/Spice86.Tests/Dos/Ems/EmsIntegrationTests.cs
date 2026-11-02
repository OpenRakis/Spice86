namespace Spice86.Tests.Dos.Ems;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for EMS functionality that run machine code through the emulation stack. Every
/// resource runs in interpreted mode with EMS enabled, and the scenario oracle checks the bytes the
/// program wrote to the shared test result ports. Setup and oracle live once in
/// <see cref="AsmScenarioCatalog"/>, so the generated entry point checks the very same expectations.
/// </summary>
public class EmsIntegrationTests {
    /// <summary>Identities of every EMS scenario.</summary>
    public static IEnumerable<object[]> EmsScenarioIds =>
        AsmScenarioCatalog.EmsScenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(EmsScenarioIds))]
    public void EmsResourceReportsSuccess(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
