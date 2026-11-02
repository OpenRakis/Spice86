namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Thin theory wrapper running every INT 16h enhanced scenario through the shared ASM pipeline.
/// The scenarios themselves are defined in <see cref="ConsoleProgramScenarioCatalog.Int16Scenarios"/>.
/// </summary>
public class KeyboardInt16EnhancedIntegrationTests {
    /// <summary>Identities of every INT 16h scenario.</summary>
    public static IEnumerable<object[]> Int16ScenarioIds =>
        ConsoleProgramScenarioCatalog.Int16Scenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>
    /// Runs a single INT 16h scenario in interpreted mode.
    /// </summary>
    /// <param name="scenarioIdentity">Identity of the scenario to run.</param>
    [Theory]
    [MemberData(nameof(Int16ScenarioIds))]
    public void RunInt16Scenario_Interpreted(string scenarioIdentity) {
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}