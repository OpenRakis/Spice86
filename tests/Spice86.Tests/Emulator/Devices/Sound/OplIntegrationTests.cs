namespace Spice86.Tests.Emulator.Devices.Sound;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for OPL timer and I/O port latency that run machine code through the full
/// emulation stack: register writes and status reads must respect the real hardware delays, so the
/// timer overflow the programs observe happens within their expected poll iteration counts. Setup,
/// sound model and the timing oracles live once in <see cref="BiosDeviceScenarioCatalog.SoundScenarios"/>,
/// so the generated entry point checks the very same iteration counts.
/// </summary>
[Trait("Category", "Sound")]
public class OplIntegrationTests {
    /// <summary>Identities of the two OPL timing scenarios.</summary>
    public static IEnumerable<object[]> OplScenarioIds =>
        BiosDeviceScenarioCatalog.OplScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>Runs one OPL program and checks its timing oracle.</summary>
    /// <param name="scenarioIdentity">Stable identity of the OPL scenario.</param>
    [Theory]
    [MemberData(nameof(OplScenarioIds))]
    public void OplTimingScenarioMatchesHardwareDelays(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.BiosDeviceScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
