namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Thin theory wrapper running every bootstrap/AUTOEXEC scenario through the shared ASM pipeline.
/// The scenarios themselves are defined in <see cref="DosBatchScenarioCatalog.BootstrapScenarios"/>.
/// </summary>
public class BootstrapAutoexecIntegrationTests {
    /// <summary>Identities of every bootstrap scenario.</summary>
    public static IEnumerable<object[]> BootstrapScenarioIds =>
        DosBatchScenarioCatalog.BootstrapScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>
    /// Runs a single bootstrap scenario in interpreted mode.
    /// </summary>
    /// <param name="scenarioIdentity">Identity of the scenario to run.</param>
    [Theory]
    [MemberData(nameof(BootstrapScenarioIds))]
    public void RunBootstrapScenario_Interpreted(string scenarioIdentity) {
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}