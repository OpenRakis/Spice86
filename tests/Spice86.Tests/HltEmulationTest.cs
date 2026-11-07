namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// HLT emulation behaviors, each in the interpreted and the JIT-compiled CPU: the STI; HLT pair waits
/// for the timer tick instead of stopping the run, and a CPU cycle breakpoint fires while the CPU is
/// halted. The basic "HLT ends the run" case is also exercised in the generated-code catalogue.
/// </summary>
public class HltEmulationTest {
    /// <summary>Both CPU execution modes.</summary>
    public static IEnumerable<object[]> JitModes => [[JitMode.InterpretedOnly], [JitMode.CompiledOnly]];

    [Theory]
    [MemberData(nameof(JitModes))]
    public void TestHlt(JitMode jitMode) {
        using (AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.Hlt(), jitMode)) {
        }
    }

    /// <summary>The STI; HLT pair waits for the timer tick, the IRQ0 handler sets DX to 1, and the
    /// program stores it after resuming past the HLT.</summary>
    [Theory]
    [MemberData(nameof(JitModes))]
    public void TestHltWaitsForHardwareInterrupt(JitMode jitMode) {
        using (AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.HltWaitsForHardwareInterrupt(), jitMode)) {
        }
    }

    /// <summary>With every PIC line masked the HLT waits forever, so the run ends only when the CPU
    /// cycle breakpoint fires while the CPU is halted.</summary>
    [Theory]
    [MemberData(nameof(JitModes))]
    public void TestHltCycleBreakpointTriggersWhileHalted(JitMode jitMode) {
        using (AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.HltCycleBreakpointWhileHalted(), jitMode)) {
        }
    }
}
