namespace Spice86.Tests.Bios;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration test for the VGA BIOS INT 10h scroll functions. Verifies that scroll-up clears the
/// full width of the vacated row. The program, the setup and the character and attribute
/// expectations live once in <see cref="BiosDeviceScenarioCatalog.VgaScenarios"/>, so the generated
/// entry point checks the very same video cells.
/// </summary>
public class VgaScrollIntegrationTests {
    /// <summary>Identities of every VGA scenario.</summary>
    public static IEnumerable<object[]> VgaScenarioIds =>
        BiosDeviceScenarioCatalog.VgaScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>Runs the scroll-up program and checks that every column of the vacated row is cleared.</summary>
    /// <param name="scenarioIdentity">Stable identity of the VGA scenario.</param>
    [Theory]
    [MemberData(nameof(VgaScenarioIds))]
    public void ScrollUpClearsEntireBottomRow(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.BiosDeviceScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle checks every copied and cleared cell
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
