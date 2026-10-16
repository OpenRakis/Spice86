namespace Spice86.Tests.Dos.Xms;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for XMS functionality that run machine code through the emulation stack, similar
/// to how real programs like HITEST.ASM interact with the XMS driver. Both resources run in interpreted
/// mode with XMS enabled, and the scenario oracle checks the bytes the program wrote to the shared test
/// result ports. Setup and oracle live once in <see cref="AsmScenarioCatalog"/>, so the generated entry
/// point checks the very same expectations.
/// </summary>
public class XmsIntegrationTests {
    /// <summary>Identities of every XMS scenario.</summary>
    public static IEnumerable<object[]> XmsScenarioIds =>
        AsmScenarioCatalog.XmsScenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(XmsScenarioIds))]
    public void XmsResourceReportsSuccess(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
