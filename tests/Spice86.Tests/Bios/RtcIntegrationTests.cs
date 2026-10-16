namespace Spice86.Tests.Bios;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Integration tests for RTC/CMOS and time services that run real assembly programs through the
/// emulation stack: direct CMOS port access, BIOS INT 1A time services, DOS INT 21h date/time
/// services, the INT 15h AH=83h event wait and the INT 70h RTC periodic interrupt configuration.
/// Every scenario comes from <see cref="BiosDeviceScenarioCatalog.RtcScenarios"/>: setup and oracle
/// live once in the shared catalogue, so the generated entry point checks the very same success
/// port, failure port and completion-marker expectations.
/// </summary>
public class RtcIntegrationTests {
    /// <summary>Identities of every RTC scenario.</summary>
    public static IEnumerable<object[]> RtcScenarioIds =>
        BiosDeviceScenarioCatalog.RtcScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>Runs one RTC program and checks its behavioral oracle.</summary>
    /// <param name="scenarioIdentity">Stable identity of the RTC scenario.</param>
    [Theory]
    [MemberData(nameof(RtcScenarioIds))]
    public void RtcScenarioProducesItsExpectedResult(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.BiosDeviceScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
