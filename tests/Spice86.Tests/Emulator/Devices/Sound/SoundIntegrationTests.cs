namespace Spice86.Tests.Emulator.Devices.Sound;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration test for the Sound Blaster DSP reset: the reset must answer 0xAA after the 20us
/// hardware delay, which the program proves by counting its poll iterations. Setup, the SBPro2
/// model and the oracle live once in <see cref="BiosDeviceScenarioCatalog.SoundBlasterScenarios"/>,
/// so the generated entry point checks the very same iteration count.
/// </summary>
[Trait("Category", "Sound")]
public class SoundIntegrationTests {
    /// <summary>Identities of the Sound Blaster reset scenarios.</summary>
    public static IEnumerable<object[]> SoundBlasterScenarioIds =>
        BiosDeviceScenarioCatalog.SoundBlasterScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>Runs the DSP reset program and checks its timing oracle.</summary>
    /// <param name="scenarioIdentity">Stable identity of the Sound Blaster scenario.</param>
    [Theory]
    [MemberData(nameof(SoundBlasterScenarioIds))]
    public void SoundBlasterDspResetProducesAAAfterHardwareDelay(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.BiosDeviceScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
