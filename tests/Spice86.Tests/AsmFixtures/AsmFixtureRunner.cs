namespace Spice86.Tests.AsmFixtures;
using Spice86.Shared.Emulator.Memory;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.ViewModels.Services;

using System.Text;

/// <summary>Runs ASM fixtures in emulated mode and checks their oracle.</summary>
internal static class AsmFixtureRunner {
    /// <summary>Runs the settings' program without checking an oracle. The run is disposed if anything throws.</summary>
    /// <param name="settings">Settings of the run.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettings(AsmRunSettings settings, JitMode jitMode) {
        AsmRunScope scope = new();
        return Execute(new AsmFixtureRun(settings.CreateEmulatedCreator(jitMode), scope), settings, scope, _ => { });
    }

    /// <summary>Runs the scope's fixture without checking an oracle. The run is disposed if anything throws.</summary>
    /// <param name="scope">Prepared run scope carrying the fixture and its observations.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettings(AsmRunScope scope, JitMode jitMode) {
        AsmRunSettings settings = scope.Fixture.Settings;
        return Execute(new AsmFixtureRun(settings.CreateEmulatedCreator(jitMode), scope), settings, scope, _ => { });
    }

    /// <summary>
    /// Runs the settings' program with an override supplier installed, without checking an oracle.
    /// The run is disposed if any post-run guard throws. After the run, it fails if none of the
    /// supplier's overrides was ever entered, which detects an installed-but-unused supplier whose
    /// program actually ran in the interpreter, and it fails if the interpreter executed anything that
    /// is not emulator-provided code, which detects a fallback of program instructions back to the
    /// interpreter.
    /// </summary>
    /// <param name="settings">Settings of the run.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettingsWithOverride(AsmRunSettings settings, IOverrideSupplier overrideSupplier) {
        return RunSettingsWithOverride(new AsmRunScope(), overrideSupplier, settings);
    }

    /// <summary>
    /// Runs the scope's fixture with an override supplier installed, without checking an oracle.
    /// The run is disposed if any post-run guard throws. After the run, it fails if none of the
    /// supplier's overrides was ever entered, and it fails if the interpreter executed application code.
    /// </summary>
    /// <param name="scope">Prepared run scope carrying the fixture and its observations.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunSettingsWithOverride(AsmRunScope scope, IOverrideSupplier overrideSupplier) {
        return RunSettingsWithOverride(scope, overrideSupplier, scope.Fixture.Settings);
    }

    /// <summary>Runs the fixture's program and checks its oracle. The run is disposed if anything throws.</summary>
    /// <param name="fixture">Fixture to run.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunEmulated(AsmFixture fixture, JitMode jitMode) {
        return AssertOracleAndReturn(RunSettings(new AsmRunScope(fixture), jitMode), fixture);
    }

    /// <summary>Runs the scope's fixture and checks its oracle. The run is disposed if anything throws.</summary>
    /// <param name="scope">Prepared run scope carrying the fixture and its observations.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunEmulated(AsmRunScope scope, JitMode jitMode) {
        return AssertOracleAndReturn(RunSettings(scope, jitMode), scope.Fixture);
    }

    /// <summary>Prepares a fresh run of the scenario, runs it and checks its oracle, so a caller only names
    /// the scenario it wants executed.</summary>
    /// <param name="scenario">Scenario to run.</param>
    /// <param name="jitMode">JIT mode to run in.</param>
    public static void RunScenario(AsmScenario scenario, JitMode jitMode) {
        using AsmRunScope scope = scenario.Prepare();
        RunEmulated(scope, jitMode).Dispose();
    }

    /// <summary>Runs the fixture's program with an override supplier installed and checks its oracle.</summary>
    /// <param name="fixture">Fixture to run.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunWithOverride(AsmFixture fixture, IOverrideSupplier overrideSupplier) {
        return AssertOracleAndReturn(RunSettingsWithOverride(fixture.Settings, overrideSupplier), fixture);
    }

    /// <summary>Runs the scope's fixture with an override supplier installed and checks its oracle.</summary>
    /// <param name="scope">Prepared run scope carrying the fixture and its observations.</param>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The finished run, owned by the caller.</returns>
    public static AsmFixtureRun RunWithOverride(AsmRunScope scope, IOverrideSupplier overrideSupplier) {
        return AssertOracleAndReturn(RunSettingsWithOverride(scope, overrideSupplier), scope.Fixture);
    }

    private static AsmFixtureRun RunSettingsWithOverride(AsmRunScope scope, IOverrideSupplier overrideSupplier,
        AsmRunSettings settings) {
        InvocationTrackingOverrideSupplier tracking = new(overrideSupplier);
        AsmFixtureRun run = new(settings.CreateOverrideCreator(tracking), scope);
        ApplicationExecutionObserver observer = new();
        scope.Register(AsmRunScope.InterpreterExecutionObservation, observer);
        bool transferredToCaller = false;
        try {
            RunProgram(run, settings, scope, fixtureRun => {
                observer.Install(fixtureRun.Machine);
                fixtureRun.DependencyInjection.FunctionCatalogue.FunctionInformations.Values
                    .Should().Contain(functionInformation => functionInformation.HasOverride);
            });
            AssertGeneratedExecution(tracking, observer, scope, settings.ScenarioIdentity, run.Machine.CpuState.Cycles);
            transferredToCaller = true;
            return run;
        } finally {
            observer.Uninstall();
            if (!transferredToCaller) {
                run.Dispose();
            }
        }
    }

    /// <summary>
    /// Checks that the program ran as generated code: when the machine executed anything, a generated override was entered
    /// and the interpreter ran no application code. A run that executes nothing (a batch file whose internal commands the
    /// DOS batch engine finished while loading) enters no override, and that is correct.
    /// </summary>
    private static void AssertGeneratedExecution(InvocationTrackingOverrideSupplier tracking,
        ApplicationExecutionObserver observer, AsmRunScope scope, string scenarioIdentity, long executedCycles) {
        if (executedCycles > 0) {
            tracking.InvokedAddresses.Should().NotBeEmpty(
                "the override supplier is installed and the machine executed instructions, but no generated override was entered, so the program ran in the interpreter");
        }
        EmulatorCodeInventory inventory =
            scope.Observations.Get<EmulatorCodeInventory>(AsmRunScope.EmulatorCodeInventoryObservation);
        ApplicationFallbackGuard.AssertNoInterpreterExecutedApplicationCode(
            observer.Executions, inventory, scenarioIdentity);
    }

    private static AsmFixtureRun Execute(AsmFixtureRun run, AsmRunSettings settings, AsmRunScope scope,
        Action<AsmFixtureRun> beforeProgram) {
        bool succeeded = false;
        try {
            RunProgram(run, settings, scope, beforeProgram);
            succeeded = true;
            return run;
        } finally {
            if (!succeeded) {
                run.Dispose();
            }
        }
    }

    private static void RunProgram(AsmFixtureRun run, AsmRunSettings settings, AsmRunScope scope,
        Action<AsmFixtureRun> beforeProgram) {
        try {
            scope.Register(AsmRunScope.EntryPointObservation, run.Machine.CpuState.IpSegmentedAddress);
            settings.ConfigureMachine(run.Machine);
            MountConfiguredDrives(settings, run.Machine);
            MountConfiguredImages(settings, run.Machine);
            PreloadKeys(settings, run.Machine);
            RegisterKeyInjections(settings, run);
            // Captured here, after the machine is fully built and its program is loaded but before the
            // program executes anything, so the emulator code it records cannot be the program's own.
            scope.Register(AsmRunScope.EmulatorCodeInventoryObservation, EmulatorCodeInventory.Capture(run.Machine));
            beforeProgram(run);
            run.DependencyInjection.ProgramExecutor.Run();
            scope.CollectObservations(run.Machine);
        } finally {
            UnmountConfiguredImages(settings, run.Machine);
        }
    }

    private static void MountConfiguredDrives(AsmRunSettings settings, Machine machine) {
        foreach ((char driveLetter, string hostFolder) in settings.MountedFolderDrives) {
            machine.Dos.DosDriveManager.MountFolderDrive(driveLetter, hostFolder);
        }
    }

    private static void MountConfiguredImages(AsmRunSettings settings, Machine machine) {
        foreach ((char driveLetter, string imagePath) in settings.MountedCdRomImages) {
            machine.Dos.DosDriveManager.MountImageAsCdRom(driveLetter, imagePath);
        }
        foreach ((char driveLetter, string imagePath) in settings.MountedFloppyImages) {
            machine.Dos.DosDriveManager.MountImageAsFloppy(driveLetter, imagePath);
        }
    }

    private static void UnmountConfiguredImages(AsmRunSettings settings, Machine machine) {
        foreach (char driveLetter in settings.MountedCdRomImages.Keys.Concat(settings.MountedFloppyImages.Keys)
                     .Where(machine.Dos.DosDriveManager.ContainsKey)) {
            machine.Dos.DosDriveManager.Unmount(driveLetter);
        }
    }

    private static void RegisterKeyInjections(AsmRunSettings settings, AsmFixtureRun run) {
        foreach (HeadlessKeyInjection injection in settings.HeadlessKeyInjections) {
            HeadlessGui headlessGui = run.DependencyInjection.HeadlessGui
                ?? throw new InvalidOperationException(
                    "HeadlessGui is not available - key injection requires HeadlessType.Minimal");
            run.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                new AddressBreakPoint(BreakPointType.CPU_CYCLES, injection.CycleCount,
                    _ => injection.InjectKeys(headlessGui), isRemovedOnTrigger: true), true);
        }
    }

    private static void PreloadKeys(AsmRunSettings settings, Machine machine) {
        foreach (ushort keyCode in settings.PreloadedKeyCodes) {
            machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer.EnqueueKeyCode(keyCode);
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
        CompareMemoryWithExpected(fixture.Settings.ScenarioIdentity, result.Memory, fixture.ExpectedMemory);
        fixture.AssertResult(result);
    }

    private static void CompareMemoryWithExpected(string scenarioIdentity, IMemory memory, byte[] expected) {
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
            throw new Xunit.Sdk.XunitException($"Memory diff for {scenarioIdentity}:\n{sb}");
        }
    }
}
