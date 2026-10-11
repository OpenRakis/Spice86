namespace Spice86.Tests.Dos;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h, AH=4Ah (Resize Memory Block): every padding variant runs in
/// interpreted mode and its scenario oracle verifies the carry flag the program observed when the
/// machine stopped. Setup and oracle live once in <see cref="AsmScenarioCatalog"/>, so the generated
/// entry point checks the very same expectation.
/// </summary>
public class DosReallocIntegrationTests {
    /// <summary>Identities of every reallocation scenario.</summary>
    public static IEnumerable<object[]> ReallocScenarioIds =>
        AsmScenarioCatalog.ReallocScenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(ReallocScenarioIds))]
    public void DosReallocSucceedsWithVariousPaddingSizes(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
