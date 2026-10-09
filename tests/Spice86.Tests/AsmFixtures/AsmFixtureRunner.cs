namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using System.Text;

/// <summary>Runs ASM fixtures in emulated mode and checks their oracle.</summary>
internal static class AsmFixtureRunner {
    /// <summary>Runs the settings' program without checking an oracle. The run is disposed if anything throws.</summary>
    /// <param name="settings">Settings of the run.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettings(AsmRunSettings settings, JitMode jitMode) {
        return Execute(new AsmFixtureRun(settings.CreateEmulatedCreator(jitMode)), settings, _ => { });
    }

    /// <summary>Runs the settings' program with an override supplier installed, without checking an oracle.</summary>
    /// <param name="settings">Settings of the run.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettingsWithOverride(AsmRunSettings settings, IOverrideSupplier overrideSupplier) {
        AsmFixtureRun run = new(settings.CreateOverrideCreator(overrideSupplier));
        return Execute(run, settings, fixtureRun =>
            fixtureRun.DependencyInjection.FunctionCatalogue.FunctionInformations.Values
                .Should().Contain(functionInformation => functionInformation.HasOverride));
    }

    /// <summary>Runs the fixture's program and checks its oracle. The run is disposed if anything throws.</summary>
    /// <param name="fixture">Fixture to run.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunEmulated(AsmFixture fixture, JitMode jitMode) {
        return AssertOracleAndReturn(RunSettings(fixture.Settings, jitMode), fixture);
    }

    /// <summary>Runs the fixture's program with an override supplier installed and checks its oracle.</summary>
    /// <param name="fixture">Fixture to run.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunWithOverride(AsmFixture fixture, IOverrideSupplier overrideSupplier) {
        AsmFixtureRun run = RunSettingsWithOverride(fixture.Settings, overrideSupplier);
        return AssertOracleAndReturn(run, fixture);
    }

    private static AsmFixtureRun Execute(AsmFixtureRun run, AsmRunSettings settings, Action<AsmFixtureRun> beforeProgram) {
        bool succeeded = false;
        try {
            settings.ConfigureMachine(run.Machine);
            beforeProgram(run);
            run.DependencyInjection.ProgramExecutor.Run();
            succeeded = true;
            return run;
        } finally {
            if (!succeeded) {
                run.Dispose();
            }
        }
    }

    private static AsmFixtureRun AssertOracleAndReturn(AsmFixtureRun run, AsmFixture fixture) {
        bool succeeded = false;
        try {
            AssertOracle(fixture, run.Result);
            succeeded = true;
            return run;
        } finally {
            if (!succeeded) {
                run.Dispose();
            }
        }
    }

    private static void AssertOracle(AsmFixture fixture, AsmFixtureResult result) {
        CompareMemoryWithExpected(fixture.Settings.BinName, result.Memory, fixture.ExpectedMemory);
        fixture.AssertResult(result);
    }

    private static void CompareMemoryWithExpected(string binName, IMemory memory, byte[] expected) {
        if (expected.Length == 0) {
            return;
        }
        byte[] actual = memory.ReadRam((uint)expected.Length);
        if (!actual.SequenceEqual(expected)) {
            StringBuilder sb = new();
            for (int i = 0; i < expected.Length; i++) {
                if (actual[i] != expected[i]) {
                    sb.AppendLine($"  [{i:X2}] expected=0x{expected[i]:X2} actual=0x{actual[i]:X2}");
                }
            }
            throw new Xunit.Sdk.XunitException($"Memory diff for {binName}:\n{sb}");
        }
    }
}
