namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Runs the native Tandy probes through both CPU execution engines.</summary>
public sealed class TandyPlatformTest {
    public static IEnumerable<object[]> Cases =>
        TandyScenarioCatalog.Scenarios.SelectMany(scenario => new[] {
            new object[] { scenario.Identity, PlatformOf(scenario), JitMode.InterpretedOnly },
            new object[] { scenario.Identity, PlatformOf(scenario), JitMode.CompiledOnly }
        });

    [Theory]
    [MemberData(nameof(Cases))]
    public void HardwareProbeCompletes(string identity, string platform, JitMode jitMode) {
        // Arrange
        AsmScenario scenario = TandyScenarioCatalog.Scenarios.Single(candidate =>
            candidate.Identity == identity && PlatformOf(candidate) == platform);
        using AsmRunScope scope = scenario.Prepare();

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, jitMode);

        // Assert
        string key = MachineGoldens.GoldenKey(scope.Fixture);
        MachineGoldens.CompareListingWithExpected(key, run.Machine);
        MachineGoldens.CompareCfgBlocksJsonWithExpected(key, run.Machine);
    }

    private static string PlatformOf(AsmScenario scenario) {
        using AsmRunScope scope = scenario.Prepare();
        return scope.Fixture.Settings.MachineType.ToString();
    }
}
