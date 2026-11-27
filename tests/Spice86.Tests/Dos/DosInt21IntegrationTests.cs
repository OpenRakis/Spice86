namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h functionality that run DOS COM resources through the emulation
/// stack. Every scenario comes from <see cref="AsmScenarioCatalog.Int21Scenarios"/>: setup and oracle
/// live once in the shared catalogue, so the generated entry point checks the very same expectations,
/// and each run prepares its own host files, disk images and input from fresh state.
/// </summary>
public class DosInt21IntegrationTests {
    /// <summary>Identities of every INT 21h scenario.</summary>
    public static IEnumerable<object[]> Int21ScenarioIds =>
        AsmScenarioCatalog.Int21Scenarios.Select(scenario => new object[] { scenario.Identity });

    [Theory]
    [MemberData(nameof(Int21ScenarioIds))]
    public void Int21ScenarioProducesItsExpectedResult(string scenarioIdentity) {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);

        // Act & Assert: the scenario's own oracle runs inside the shared runner
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }

    /// <summary>
    /// AH=50h/55h setting the current PSP is a recorded run fact: the CFG reload dump carries it
    /// and the reloading run merges it before the program runs again.
    /// </summary>
    [Fact]
    public void ProgramSetCurrentPspSurvivesCfgReload() {
        // psp_switch_frame_reuse calls AH=55h and AH=50h during its run.
        using TempFile recordedDataDirectory = new(nameof(ProgramSetCurrentPspSurvivesCfgReload));
        AsmScenario scenario = AsmScenarioCatalog.DosScenario("psp_switch_frame_reuse")
            .WithOracle(fixture => new AsmFixture(
                fixture.Settings with { RecordedDataDirectory = recordedDataDirectory.Path },
                fixture.ExpectedMemory, fixture.AssertResult));
        using AsmRunScope scope = scenario.Prepare();
        AsmRunSettings settings = scope.Fixture.Settings;

        using (AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly)) {
            run.DependencyInjection.GeneratorInputsCollector
                .Capture(run.Machine.CfgCpu.ExecutionContextManager).HardwareInterrupts.ProgramSetCurrentPsp
                .Should().BeTrue("the program called AH=50h/55h during the first run");
        }

        // AsmFixtureRun builds the emulator without running the program: the reload merged the
        // dump's fact before the program runs again.
        using AsmFixtureRun reloaded = new(
            (settings with { ReloadCfgGraph = true }).CreateEmulatedCreator(JitMode.InterpretedOnly),
            new AsmRunScope());
        reloaded.DependencyInjection.GeneratorInputsCollector
            .Capture(reloaded.Machine.CfgCpu.ExecutionContextManager).HardwareInterrupts.ProgramSetCurrentPsp
            .Should().BeTrue("the reloading run merged the programSetCurrentPsp fact the dump recorded");
    }
}
