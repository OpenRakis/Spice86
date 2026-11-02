namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Thin theory wrapper running every console/IOCTL scenario through the shared ASM pipeline.
/// The scenarios themselves are defined in <see cref="ConsoleProgramScenarioCatalog.ConsoleScenarios"/>.
/// </summary>
public class DosConsoleIoctlIntegrationTests {
    /// <summary>Identities of every console program scenario.</summary>
    public static IEnumerable<object[]> ConsoleScenarioIds =>
        ConsoleProgramScenarioCatalog.ConsoleScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>
    /// Runs a single console program scenario in interpreted mode.
    /// </summary>
    /// <param name="scenarioIdentity">Identity of the scenario to run.</param>
    [Theory]
    [MemberData(nameof(ConsoleScenarioIds))]
    public void RunConsoleProgramScenario_Interpreted(string scenarioIdentity) {
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }
}