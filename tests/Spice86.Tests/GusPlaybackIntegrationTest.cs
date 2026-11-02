namespace Spice86.Tests;

using Spice86.Core.CLI;

using Spice86.Tests.AsmFixtures;

using Xunit;

public sealed class GusPlaybackIntegrationTest {
    [Fact]
    public void GuestProgramsProducePannedEightAndSixteenBitPlayback() {
        AsmScenario scenario = AsmScenarioCatalog.BiosDeviceScenario("gus_playback_samples");

        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}
