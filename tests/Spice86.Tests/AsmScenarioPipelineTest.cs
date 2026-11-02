namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Spice86.Tests.AsmFixtures;
using Spice86.Tests.Dos;
using Spice86.Tests.Utility;

using Xunit;
using Xunit.Sdk;

/// <summary>
/// Infrastructure guarantees of the shared scenario pipeline: one oracle for discovery, interpreted and
/// generated runs, proof that generated code was entered, stable path-independent identity, and a fresh
/// filesystem, input stream and observation handlers per run.
/// </summary>
public sealed class AsmScenarioPipelineTest {
    private const string ChildProgramFileName = "GUARDCHILD.COM";
    private const string DetailScenarioIdentity = "standard_file_handles_inherited";
    private const string LatchObservation = "latch";
    private const string TestPortResultsObservation = "testPortResults";
    private const string WorkingDirectoryObservation = "workingDirectory";
    private const byte TestPortFailure = 0xFF;
    private const byte TestPortSuccess = 0x00;

    /// <summary>Longest x86 instruction, the most bytes worth reading at one address.</summary>
    private const int MaxInstructionBytes = 15;

    /// <summary>A deliberately wrong expected value must fail the interpreted run and the
    /// generated execution run (after compilation), not just during discovery.</summary>
    [Fact]
    public void ADeliberatelyWrongOracleFailsTheInterpretedAndTheGeneratedRun() {
        // Arrange: the real scenario with its CORRECT oracle for discovery
        AsmScenario correctScenario = CorrectJmpMovScenario();
        AsmScenario wrongScenario = WrongJmpMovScenario();

        // Act & Assert: interpreted run with wrong oracle fails
        AssertInterpretedRunFailsWith(wrongScenario, "*Memory diff for jmpmov*");

        // Act & Assert: generated execution with wrong oracle fails after a real compilation
        GeneratedPipelineStages stages = new();
        using CompiledGeneratedOverride compiledOverride = DiscoverAndCompile(correctScenario, stages);
        Action generatedRun = () => RunCompiledExecution(wrongScenario, compiledOverride, stages);
        generatedRun.Should().Throw<XunitException>().WithMessage("*Memory diff for jmpmov*");
        stages.CompiledExecutionCompleted.Should().BeFalse("the wrong oracle must fail the compiled execution");

        // Positive control: generated execution with correct oracle passes
        RunCompiledExecution(correctScenario, compiledOverride, stages);
        stages.CompiledExecutionCompleted.Should().BeTrue("the correct oracle passes the compiled execution");
    }

    /// <summary>An override supplier that is installed but never entered must fail the run, even though the
    /// interpreter alone would produce exactly the right result.</summary>
    [Fact]
    public void AnInstalledButUnusedOverrideFailsTheRun() {
        // Arrange
        AsmFixture fixture = AsmFixtureCatalog.JmpMov();

        // Act
        Action act = () =>
            AsmFixtureRunner.RunSettingsWithOverride(fixture.Settings, new NeverCalledOverrideSupplier()).Dispose();

        // Assert: jmpmov ends with the right memory either way, so only the invocation proof can fail this.
        act.Should().Throw<XunitException>().WithMessage("*no generated override was entered*");
    }

    /// <summary>An override supplier invoked at the application entry point but leaving the application
    /// body to the interpreter must be rejected through the ordinary runner, with no tracking argument.</summary>
    [Fact]
    public void AnInvokedEntryPointOverrideThatFallsBackToInterpreterMustBeRejected() {
        // Arrange: the real scenario, whose interpreter run passes its own oracle first
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(DetailScenarioIdentity);
        SegmentedAddress entryPoint = InterpretedEntryPointOf(scenario);
        InvocationTrackingOverrideSupplier tracking = new(new EntryPointOnlyOverrideSupplier(entryPoint));

        // Act
        using AsmRunScope scope = scenario.Prepare();
        Action act = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert: the run is rejected for the fallback, naming the scenario and the offending CS:IP
        act.Should().Throw<XunitException>().WithMessage($"*'{DetailScenarioIdentity}'*{CsIp(entryPoint)}*");
        tracking.InvokedAddresses.Should().Contain(entryPoint, "the entry point delegate must have been invoked");
    }

    /// <summary>A CPU test image is loaded into the BIOS address range instead of DOS conventional memory,
    /// but it is still the test program's application code: a supplier invoked at its entry point whose
    /// delegate does nothing must be rejected, because the interpreter then runs the whole image.</summary>
    [Fact]
    public void ACpuImageCannotPassThroughANoOpOverride() {
        // Arrange: the real jmpmov scenario, whose interpreter run passes its own oracle first
        AsmScenario scenario = CorrectJmpMovScenario();
        SegmentedAddress entryPoint = InterpretedEntryPointOf(scenario);
        InvocationTrackingOverrideSupplier tracking = new(new EntryPointOnlyOverrideSupplier(entryPoint));
        using AsmRunScope scope = scenario.Prepare();

        // Act
        Action run = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert: the run is rejected for interpreter fallback, not for a wrong oracle or a missing delegate
        run.Should().Throw<XunitException>()
            .WithMessage($"*'{scenario.Identity}'*fell back to the interpreter*");
        tracking.InvokedAddresses.Should().Contain(entryPoint, "the entry point delegate must have been invoked");
    }

    /// <summary>A CPU image instruction that is not the entry point must be rejected too: redirecting the
    /// entry override away from the reset vector leaves an interior instruction of the image to the
    /// interpreter, and that instruction is application code regardless of where the image is loaded.</summary>
    [Fact]
    public void ACpuImageInteriorInstructionLeftToTheInterpreterIsRejected() {
        // Arrange: an entry-fallback run of the CPU image proves which image instruction follows the entry
        AsmScenario scenario = CorrectJmpMovScenario();
        SegmentedAddress entryPoint = InterpretedEntryPointOf(scenario);
        SegmentedAddress interiorInstruction = ApplicationInstructionAfterEntry(scenario, entryPoint);
        InvocationTrackingOverrideSupplier tracking =
            new(new RedirectFromEntryOverrideSupplier(entryPoint, interiorInstruction));
        using AsmRunScope scope = scenario.Prepare();

        // Act
        Action run = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert
        XunitException failure = run.Should().Throw<XunitException>().Which;
        tracking.InvokedAddresses.Should().Contain(entryPoint, "the entry point delegate must have been invoked");
        failure.Message.Should().Contain(CsIp(interiorInstruction),
            "the interpreter-executed image instruction must be named");
        failure.Message.Should().NotContain(CsIp(entryPoint),
            "the entry point was redirected away, so it cannot be the instruction that was interpreted");
    }

    /// <summary>An override that redirects the entry point to a later application instruction never lets
    /// the interpreter revisit the entry point, yet every instruction it does leave to the interpreter is
    /// application code, so the run must still be rejected.</summary>
    [Fact]
    public void AnOverrideLeavingALaterApplicationInstructionToTheInterpreterIsRejected() {
        // Arrange: an entry-fallback run proves which application instruction really follows the entry point
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(DetailScenarioIdentity);
        SegmentedAddress entryPoint = InterpretedEntryPointOf(scenario);
        SegmentedAddress interiorInstruction = ApplicationInstructionAfterEntry(scenario, entryPoint);
        InvocationTrackingOverrideSupplier tracking =
            new(new RedirectFromEntryOverrideSupplier(entryPoint, interiorInstruction));

        // Act
        using AsmRunScope scope = scenario.Prepare();
        Action act = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert
        XunitException failure = act.Should().Throw<XunitException>().Which;
        tracking.InvokedAddresses.Should().Contain(entryPoint, "the entry point delegate must have been invoked");
        failure.Message.Should().Contain(CsIp(interiorInstruction),
            "the interpreter-executed application instruction must be named");
        failure.Message.Should().NotContain(CsIp(entryPoint),
            "the entry point was redirected away, so it cannot be the instruction that was interpreted");
    }

    /// <summary>A delegate installed on emulator-provided code is not application coverage: when the
    /// application itself stays interpreted the run must be rejected even though that delegate was invoked.</summary>
    [Fact]
    public void ANonApplicationOverrideDoesNotCoverAnInterpretedApplication() {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(DetailScenarioIdentity);
        NonApplicationCallbackOverrideSupplier supplier = new();
        InvocationTrackingOverrideSupplier tracking = new(supplier);

        // Act
        using AsmRunScope scope = scenario.Prepare();
        Action act = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert
        act.Should().Throw<XunitException>().WithMessage($"*'{DetailScenarioIdentity}'*");
        supplier.WasInstalled.Should().BeTrue("the supplier must have been asked for its overrides");
        EmulatorCodeInventory inventory =
            scope.Observations.Get<EmulatorCodeInventory>(AsmRunScope.EmulatorCodeInventoryObservation);
        inventory.IsEmulatorProvided(supplier.InstalledAddress, supplier.InstalledBytes).Should().BeTrue(
            $"{supplier.InstalledAddress} is emulator-provided code, not application code");
        tracking.InvokedAddresses.Should().Contain(supplier.InstalledAddress,
            "the emulator-provided delegate must have been invoked for this regression to discriminate");
    }

    /// <summary>A parent override that runs while the child it EXECed stays in the interpreter must be
    /// rejected: an EXEC child is application code the generated run has to cover as well.</summary>
    [Fact]
    public void AnExecChildLeftToTheInterpreterIsRejected() {
        // Arrange: the EXEC scenario's interpreter run passes its real oracle and yields the parent entry
        AsmScenario scenario = AsmScenarioCatalog.FcbChild();
        SegmentedAddress parentEntry = InterpretedEntryPointOf(scenario);
        using AsmRunScope scope = scenario.Prepare();
        string childProgram = ChildHaltProgramIn(scope);
        ExecChildFallbackOverrideSupplier supplier = new(parentEntry, childProgram);
        InvocationTrackingOverrideSupplier tracking = new(supplier);

        // Act
        Action act = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();

        // Assert
        XunitException failure = act.Should().Throw<XunitException>().Which;
        supplier.ChildLoadError.Should().BeNull(
            $"the EXEC child must load for this regression to discriminate, but it failed with {supplier.ChildLoadError}");
        tracking.InvokedAddresses.Should().Contain(parentEntry, "the parent entry delegate must have been invoked");
        failure.Message.Should().Contain(CsIp(supplier.ChildEntry),
            "the interpreter-executed child entry must be named as the offending address");
    }

    /// <summary>A fallback rejection after otherwise successful emulation must still end the observer's
    /// lifetime and dispose the emulator, so nothing of the rejected run stays reachable.</summary>
    [Fact]
    public void AFallbackRejectionDisposesTheEmulatorAndEndsTheObservation() {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(DetailScenarioIdentity);
        SegmentedAddress entryPoint = InterpretedEntryPointOf(scenario);
        MachineObservingOverrideSupplier supplier = new(entryPoint);

        // Act
        using AsmRunScope scope = scenario.Prepare();
        Action act = () => AsmFixtureRunner.RunWithOverride(scope, supplier).Dispose();
        act.Should().Throw<XunitException>().WithMessage("*fell back to the interpreter*");

        // Assert
        ApplicationExecutionObserver observer =
            scope.Observations.Get<ApplicationExecutionObserver>(AsmRunScope.InterpreterExecutionObservation);
        observer.IsInstalled.Should().BeFalse("the observer lives for exactly one run, successful or not");
        string workingDirectory = scope.Fixture.Settings.CDrive
            ?? throw new InvalidOperationException("the scenario must mount a working directory as C:");
        Directory.Exists(workingDirectory).Should().BeTrue("only the emulator run is disposed, not the preparation");
        WeakReference? machineReference = supplier.MachineReference;
        machineReference.Should().NotBeNull("the supplier must have seen the machine it ran on");
        MachineLeakTracker.CollectUnreachable();
        machineReference?.IsAlive.Should().BeFalse(
            "the rejected run must dispose the emulator so its machine graph becomes collectible");
    }

    /// <summary>Two scenarios that execute a program named START.BAT must not collide on identity or goldens.</summary>
    [Fact]
    public void TwoScenariosNamedStartBatHaveDifferentIdentities() {
        // Arrange
        using AsmRunScope first = StartBatScenario("start_batch_alpha", 'A').Prepare();
        using AsmRunScope second = StartBatScenario("start_batch_beta", 'B').Prepare();

        // Assert
        first.Fixture.Settings.BinName.Should().EndWith("START.BAT");
        second.Fixture.Settings.BinName.Should().EndWith("START.BAT");
        first.Fixture.Settings.ScenarioIdentity.Should().Be("start_batch_alpha");
        second.Fixture.Settings.ScenarioIdentity.Should().Be("start_batch_beta");
        GeneratedCodeMachineTestRunner.GoldenKey(first.Fixture.Settings).Should()
            .NotBe(GeneratedCodeMachineTestRunner.GoldenKey(second.Fixture.Settings),
                "same-named programs of different scenarios must get distinct goldens");
    }

    /// <summary>The same scenario prepared twice, in two different directories, keeps one identity and one
    /// set of golden keys while its host paths differ.</summary>
    [Fact]
    public void TheSameScenarioPreparedInDifferentDirectoriesKeepsItsIdentity() {
        // Arrange
        AsmScenario scenario = AsmScenarioCatalog.TsrExitCode("tsr22h.com", 0);

        // Act
        using AsmRunScope first = scenario.Prepare();
        using AsmRunScope second = scenario.Prepare();

        // Assert
        first.Fixture.Settings.BinName.Should().NotBe(second.Fixture.Settings.BinName,
            "each preparation gets its own working directory");
        first.Fixture.Settings.ScenarioIdentity.Should().Be(second.Fixture.Settings.ScenarioIdentity);
        GeneratedCodeMachineTestRunner.GoldenKey(first.Fixture.Settings).Should()
            .Be(GeneratedCodeMachineTestRunner.GoldenKey(second.Fixture.Settings));
        MachineGoldens.GoldenKey(first.Fixture).Should().Be(MachineGoldens.GoldenKey(second.Fixture));
    }

    /// <summary>A file the discovery run mutates must not be visible to the generated execution: each run
    /// prepares an independent directory, and the scope deletes it on dispose.</summary>
    [Fact]
    public void AFileMutatedByDiscoveryIsNotVisibleToTheGeneratedRun() {
        // Arrange
        AsmScenario scenario = SeedFileScenario("dos_seed_file_isolation");

        // Act: discovery runs, then mutates its seed file as a program would mutate its host directory.
        using AsmRunScope discovery = scenario.Prepare();
        using (AsmFixtureRunner.RunEmulated(discovery, JitMode.InterpretedOnly)) {
        }
        string discoveryDirectory = discovery.Observations.Get<string>(WorkingDirectoryObservation);
        File.WriteAllText(Path.Join(discoveryDirectory, "SEED.TXT"), "mutated by discovery");

        // Assert: the fresh preparation sees the original file in a different directory.
        using AsmRunScope execution = scenario.Prepare();
        string executionDirectory = execution.Observations.Get<string>(WorkingDirectoryObservation);
        executionDirectory.Should().NotBe(discoveryDirectory);
        File.ReadAllText(Path.Join(executionDirectory, "SEED.TXT")).Should().Be("original");
        using (AsmFixtureRunner.RunEmulated(execution, JitMode.InterpretedOnly)) {
        }

        execution.Dispose();
        Directory.Exists(executionDirectory).Should().BeFalse("the scope deletes its working directory on dispose");
    }

    /// <summary>An input stream one run consumed must be available to the next run: the discovery run and the
    /// generated execution each preload their own key for the catalogue scenario that consumes one key and
    /// writes it to video, and both runs check the same video oracle.</summary>
    [Fact]
    public void AConsumedInputStreamIsFreshForTheGeneratedRun() {
        // Arrange & Act: the pipeline prepares discovery and execution independently, each with its own
        // fresh keyboard state, so the key the first run consumed is still there for the second.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            AsmScenarioCatalog.DosScenario("console_ah07_read_key"));
    }

    /// <summary>Test-port results must be captured per run: each run installs its own handler, and neither
    /// accumulates the other run's writes.</summary>
    [Fact]
    public void PortResultsAreCapturedIndependentlyPerRun() {
        // Arrange
        GeneratedCodeMachineTestRunner runner = new();
        AsmScenario scenario = RecordedLatchScenario();

        // Act
        using AsmRunScope discovery = scenario.Prepare();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        using CompiledGeneratedOverride compiledOverride =
            runner.CompileGeneratedProgram(discovery.Fixture.Settings, generatedProgram);
        using AsmRunScope execution = scenario.Prepare();
        using (AsmFixtureRunner.RunWithOverride(execution, compiledOverride.Supplier)) {
        }

        // Assert
        LatchIoPortHandler discoveryLatch = discovery.Observations.Get<LatchIoPortHandler>(LatchObservation);
        LatchIoPortHandler executionLatch = execution.Observations.Get<LatchIoPortHandler>(LatchObservation);
        executionLatch.Should().NotBeSameAs(discoveryLatch, "each run installs its own capture handler");
        discoveryLatch.WrittenValues.Should().NotBeEmpty("the program writes to the latch port while it runs");
        executionLatch.WrittenValues.Should().HaveCount(discoveryLatch.WrittenValues.Count,
            "each run records exactly its own port writes, never accumulated across runs");
    }

    /// <summary>The registered catalogue is enumerable and every scenario prepares a fixture with an oracle
    /// and a non-empty stable identity.</summary>
    [Fact]
    public void EveryCatalogueScenarioPreparesARunnableScenarioWithAStableIdentity() {
        // Arrange
        List<AsmScenario> scenarios = [.. AsmScenarioCatalog.All];

        // Act & Assert
        scenarios.Should().NotBeEmpty();
        foreach (AsmScenario scenario in scenarios) {
            using AsmRunScope scope = scenario.Prepare();
            scenario.Identity.Should().NotContain(Path.DirectorySeparatorChar.ToString(),
                "an identity never contains a host path");
            scope.Fixture.Settings.ScenarioIdentity.Should().Be(scenario.Identity);
            scope.Fixture.ExpectedMemory.Should().NotBeNull();
        }
    }

    /// <summary>Every INT 21h scenario runs as C:\TEST.COM, so its stable identity must carry the golden
    /// keys: two different programs copied to TEST.COM must never share a generated-source or machine
    /// golden key.</summary>
    [Fact]
    public void EveryInt21ScenarioHasCollisionFreeGoldenKeys() {
        // Arrange
        List<string> generatedKeys = new();
        List<string> machineKeys = new();

        // Act
        foreach (AsmScenario scenario in AsmScenarioCatalog.Int21Scenarios) {
            using AsmRunScope scope = scenario.Prepare();
            scope.Fixture.Settings.BinName.Should().EndWith("TEST.COM",
                "every INT 21h program runs from the same C:\\TEST.COM file name");
            generatedKeys.Add(GeneratedCodeMachineTestRunner.GoldenKey(scope.Fixture.Settings));
            machineKeys.Add(MachineGoldens.GoldenKey(scope.Fixture));
        }

        // Assert
        generatedKeys.Should().OnlyHaveUniqueItems(
            "distinct INT 21h programs copied to TEST.COM must get distinct generated-source goldens");
        machineKeys.Should().OnlyHaveUniqueItems(
            "distinct INT 21h programs copied to TEST.COM must get distinct machine goldens");
    }

    /// <summary>A deliberately wrong detail-byte expectation must fail the interpreted run and the
    /// generated execution run (after compilation), not just during discovery. Both runs use the one
    /// catalogue scenario that reports the handle detail bytes, with the wrong expectation derived
    /// from it, so identity, settings, setup and per-run observations are identical.</summary>
    [Fact]
    public void AWrongDetailByteExpectationFailsBothModes() {
        // Arrange: the real catalogue scenario with its CORRECT oracle for discovery
        AsmScenario correctScenario = AsmScenarioCatalog.DosScenario(DetailScenarioIdentity);
        AsmScenario wrongScenario = WrongDetailScenarioOf(correctScenario);

        // Act & Assert: interpreted run with wrong oracle fails
        AssertInterpretedRunFailsWith(wrongScenario, "*deliberately wrong detail-byte expectation*");

        // Act & Assert: generated execution with wrong oracle fails after a real compilation
        GeneratedPipelineStages stages = new();
        using CompiledGeneratedOverride compiledOverride = DiscoverAndCompile(correctScenario, stages);
        Action generatedRun = () => RunCompiledExecution(wrongScenario, compiledOverride, stages);
        generatedRun.Should().Throw<XunitException>().WithMessage("*deliberately wrong detail-byte expectation*");
        stages.CompiledExecutionCompleted.Should().BeFalse("the wrong oracle must fail the compiled execution");

        // Positive control: generated execution with correct oracle passes
        RunCompiledExecution(correctScenario, compiledOverride, stages);
        stages.CompiledExecutionCompleted.Should().BeTrue("the correct oracle passes the compiled execution");
    }

    /// <summary>A deliberately wrong VGA attribute must fail the interpreted run and the
    /// generated execution run (after compilation), not just during discovery.</summary>
    [Fact]
    public void AWrongVgaAttributeExpectationFailsBothModes() {
        // Arrange: the real scenario with its CORRECT oracle for discovery
        AsmScenario correctScenario = AsmScenarioCatalog.BiosDeviceScenario("scroll_up_clears_full_row");
        AsmScenario wrongScenario = correctScenario.WithOracle(fixture => new AsmFixture(fixture.Settings, fixture.ExpectedMemory, result =>
            VideoMemoryReader.ReadVideoCell(result.Memory, 2, 40).Should().Be((' ', 0x1F),
                "deliberately wrong VGA attribute expectation")));

        // Act & Assert: interpreted run with wrong oracle fails
        AssertInterpretedRunFailsWith(wrongScenario, "*deliberately wrong VGA attribute*");

        // Act & Assert: generated execution with wrong oracle fails after a real compilation
        GeneratedPipelineStages stages = new();
        using CompiledGeneratedOverride compiledOverride = DiscoverAndCompile(correctScenario, stages);
        Action generatedRun = () => RunCompiledExecution(wrongScenario, compiledOverride, stages);
        generatedRun.Should().Throw<XunitException>().WithMessage("*deliberately wrong VGA attribute*");
        stages.CompiledExecutionCompleted.Should().BeFalse("the wrong oracle must fail the compiled execution");

        // Positive control: generated execution with correct oracle passes
        RunCompiledExecution(correctScenario, compiledOverride, stages);
        stages.CompiledExecutionCompleted.Should().BeTrue("the correct oracle passes the compiled execution");
    }

    /// <summary>A deliberately wrong sound timing expectation must fail the interpreted run and the
    /// generated execution run (after compilation), not just during discovery.</summary>
    [Fact]
    public void AWrongSoundTimingExpectationFailsBothModes() {
        // Arrange: the real scenario with its CORRECT oracle for discovery
        AsmScenario correctScenario = AsmScenarioCatalog.BiosDeviceScenario("opl_write_delay");
        AsmScenario wrongScenario = correctScenario.WithOracle(fixture => new AsmFixture(fixture.Settings, fixture.ExpectedMemory, result =>
            result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation).Details[0]
                .Should().Be(2, "deliberately wrong sound iteration count expectation")));

        // Act & Assert: interpreted run with wrong oracle fails
        AssertInterpretedRunFailsWith(wrongScenario, "*deliberately wrong sound iteration count*");

        // Act & Assert: generated execution with wrong oracle fails after a real compilation
        GeneratedPipelineStages stages = new();
        using CompiledGeneratedOverride compiledOverride = DiscoverAndCompile(correctScenario, stages);
        Action generatedRun = () => RunCompiledExecution(wrongScenario, compiledOverride, stages);
        generatedRun.Should().Throw<XunitException>().WithMessage("*deliberately wrong sound iteration count*");
        stages.CompiledExecutionCompleted.Should().BeFalse("the wrong oracle must fail the compiled execution");

        // Positive control: generated execution with correct oracle passes
        RunCompiledExecution(correctScenario, compiledOverride, stages);
        stages.CompiledExecutionCompleted.Should().BeTrue("the correct oracle passes the compiled execution");
    }

    /// <summary>A wrong expectation must fail during discovery, before any generated source exists,
    /// before compilation and before compiled application execution. This is the guarantee the tests
    /// above deliberately skip by compiling a correct discovery first.</summary>
    [Fact]
    public void AWrongOracleFailsDiscoveryBeforeAnyCompilation() {
        // Arrange
        AsmScenario wrongScenario = WrongJmpMovScenario();
        GeneratedPipelineStages stages = new();

        // Act
        Action pipeline = () => {
            using CompiledGeneratedOverride compiledOverride = DiscoverAndCompile(wrongScenario, stages);
            RunCompiledExecution(wrongScenario, compiledOverride, stages);
        };

        // Assert
        pipeline.Should().Throw<XunitException>().WithMessage("*Memory diff for jmpmov*");
        stages.DiscoveryCompleted.Should().BeFalse("discovery must fail on the wrong expectation");
        stages.CompilationCompleted.Should().BeFalse("nothing may be compiled after a failed discovery");
        stages.CompiledExecutionCompleted.Should().BeFalse("no compiled application execution may follow a failed discovery");
    }

    /// <summary>If Prepare() creates a directory and then throws, the directory must be cleaned up
    /// and the original exception propagated.</summary>
    [Fact]
    public void PrepareFailureCleansUpScopeAndPropagatesException() {
        // Arrange: a scenario whose preparation creates a directory and then throws
        const string identity = "prepare_failure_cleanup";
        string? capturedDirectory = null;
        AsmScenario throwingScenario = new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            capturedDirectory = directory;
            File.WriteAllText(Path.Join(directory, "test.txt"), "created");
            throw new InvalidOperationException("deliberate preparation failure");
        }, []);

        // Act & Assert: Prepare() should propagate the exception and clean up the directory
        Action act = () => throwingScenario.Prepare();
        act.Should().Throw<InvalidOperationException>().WithMessage("deliberate preparation failure");

        // Assert: the directory should be cleaned up
        capturedDirectory.Should().NotBeNull("directory should have been created");
        Directory.Exists(capturedDirectory).Should().BeFalse("scope should clean up directory on preparation failure");
    }

    /// <summary>Successful preparation retains ownership until caller disposes; failure of one
    /// preparation does not delete a separately prepared run's directory.</summary>
    [Fact]
    public void SuccessfulAndFailedPreparationsMaintainCorrectOwnership() {
        // Arrange: two scenarios, one that succeeds and one that fails
        const string successIdentity = "prepare_success";
        const string failIdentity = "prepare_fail";
        string? successDirectory = null;
        string? failDirectory = null;

        AsmScenario successScenario = new AsmScenario(successIdentity, scope => {
            string directory = scope.CreateWorkingDirectory(successIdentity);
            successDirectory = directory;
            File.WriteAllText(Path.Join(directory, "success.txt"), "ok");
            return AsmFixtureCatalog.JmpMov(); // Use a simple valid fixture
        }, []);

        AsmScenario failScenario = new AsmScenario(failIdentity, scope => {
            string directory = scope.CreateWorkingDirectory(failIdentity);
            failDirectory = directory;
            File.WriteAllText(Path.Join(directory, "fail.txt"), "will fail");
            throw new InvalidOperationException("deliberate failure");
        }, []);

        // Act: prepare the successful one first
        using AsmRunScope successScope = successScenario.Prepare();
        successScope.Should().NotBeNull();
        Directory.Exists(successDirectory).Should().BeTrue("successful preparation's directory should exist");

        // Act: prepare the failing one (should not affect the successful one's directory)
        Action act = () => failScenario.Prepare();
        act.Should().Throw<InvalidOperationException>();

        // Assert: successful preparation's directory still exists, failed one is cleaned up
        Directory.Exists(successDirectory).Should().BeTrue("successful preparation's directory should still exist after another preparation fails");
        Directory.Exists(failDirectory).Should().BeFalse("failed preparation's directory should be cleaned up");

        // Clean up successful scope
        successScope.Dispose();
        Directory.Exists(successDirectory).Should().BeFalse("successful scope's directory should be cleaned up on dispose");
    }

    /// <summary>Console and INT 16h scenario subsets are disjoint and their union equals the complete
    /// collection, with no duplicate identities.</summary>
    [Fact]
    public void ConsoleAndInt16ScenarioSubsetsAreDisjointAndComplete() {
        // Arrange
        IReadOnlyList<AsmScenario> consoleScenarios = ConsoleProgramScenarioCatalog.ConsoleScenarios;
        IReadOnlyList<AsmScenario> int16Scenarios = ConsoleProgramScenarioCatalog.Int16Scenarios;
        IReadOnlyList<AsmScenario> allScenarios = ConsoleProgramScenarioCatalog.Scenarios;

        // Assert: subsets are disjoint
        HashSet<string> consoleIdentities = [.. consoleScenarios.Select(scenario => scenario.Identity)];
        HashSet<string> int16Identities = [.. int16Scenarios.Select(scenario => scenario.Identity)];
        consoleIdentities.Should().NotIntersectWith(int16Identities, "console and INT 16h subsets must be disjoint");

        // Assert: union equals complete collection, compared as sets rather than ordered enumerations
        HashSet<string> unionIdentities = [.. consoleIdentities.Union(int16Identities)];
        HashSet<string> allIdentities = [.. allScenarios.Select(scenario => scenario.Identity)];
        unionIdentities.Should().BeEquivalentTo(allIdentities, "union of subsets must equal complete collection");

        // Assert: no duplicate identities in complete collection
        allIdentities.Count.Should().Be(allScenarios.Count, "all scenarios must have unique identities");
    }

    private static AsmScenario WrongJmpMovScenario() {
        return AsmScenario.FromFixture("jmpmov", () => {
            AsmFixture correct = AsmFixtureCatalog.JmpMov();
            byte[] wrongExpectedMemory = [0x00, 0x00];
            return new AsmFixture(correct.Settings, wrongExpectedMemory, AsmFixture.NoResultAssertion);
        });
    }

    private static AsmScenario CorrectJmpMovScenario() {
        return AsmScenario.FromFixture("jmpmov", () => AsmFixtureCatalog.JmpMov());
    }

    /// <summary>The same catalogue scenario with a detail-byte expectation no run of it can satisfy.
    /// Identity, settings, setup and per-run observation handlers stay those of the correct scenario.</summary>
    /// <param name="correctScenario">Catalogue scenario whose detail bytes are reported.</param>
    private static AsmScenario WrongDetailScenarioOf(AsmScenario correctScenario) {
        return correctScenario.WithOracle(fixture => new AsmFixture(fixture.Settings, fixture.ExpectedMemory, result => {
            TestIoPortHandler handler = result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
            handler.Results.Should().Contain(TestPortSuccess, "the program must report success before the detail check");
            handler.Results.Should().NotContain(TestPortFailure, "the program must not report failure");
            handler.Details.Take(3).Should().Equal(new byte[] { 0, 1, 3 },
                "deliberately wrong detail-byte expectation that no run of this scenario produces");
        }));
    }

    private static AsmScenario StartBatScenario(string identity, char expectedCell) {
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string startBat = Path.Join(directory, "START.BAT");
            File.WriteAllText(startBat, $"ECHO {expectedCell}");
            AsmRunSettings settings = new() {
                BinName = startBat,
                ScenarioName = identity,
                InstallInterruptVectors = true
            };
            return new AsmFixture(settings, [], result =>
                VideoMemoryReader.ReadCells(result.Memory, 1).Should().Be(expectedCell.ToString(),
                    "the batch writes its expected cell"));
        }, []);
    }

    private static AsmScenario SeedFileScenario(string identity) {
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            scope.Register(WorkingDirectoryObservation, directory);
            string program = Path.Join(directory, "EXIT.COM");
            File.Copy(Path.Join(AppContext.BaseDirectory, "Resources", "DosConsoleProgramTests", "exit_code_0.com"),
                program, overwrite: true);
            File.WriteAllText(Path.Join(directory, "SEED.TXT"), "original");
            AsmRunSettings settings = new() {
                BinName = program,
                ScenarioName = identity,
                InstallInterruptVectors = true
            };
            return new AsmFixture(settings, [], result =>
                File.ReadAllText(Path.Join(directory, "SEED.TXT")).Should().Be("original",
                    "a run must not observe the file another run of the same scenario mutated"));
        }, []);
    }

    private static AsmScenario RecordedLatchScenario() {
        return new AsmScenario("ioports", scope => {
            AsmFixture baseFixture = AsmFixtureCatalog.IoPorts();
            AsmRunSettings settings = baseFixture.Settings with {
                ConfigureMachine = machine => {
                    LatchIoPortHandler latch = new(
                        machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher, 0xE0);
                    scope.Register(LatchObservation, latch);
                }
            };
            return new AsmFixture(settings, baseFixture.ExpectedMemory, result =>
                result.Observations.Get<LatchIoPortHandler>(LatchObservation).WrittenValues.Should().NotBeEmpty(
                    "the program writes to the latch port during the run"));
        }, []);
    }

    /// <summary>Runs the scenario in the interpreter so its own oracle passes, then reports the CS:IP the
    /// machine starts the program at.</summary>
    private static SegmentedAddress InterpretedEntryPointOf(AsmScenario scenario) {
        using AsmRunScope scope = scenario.Prepare();
        using (AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly)) {
        }
        return scope.Observations.Get<SegmentedAddress>(AsmRunScope.EntryPointObservation);
    }

    /// <summary>Runs the entry fallback once and returns the application instruction the interpreter
    /// executes after the entry point, which is therefore a proven instruction boundary of that program.</summary>
    private static SegmentedAddress ApplicationInstructionAfterEntry(AsmScenario scenario, SegmentedAddress entryPoint) {
        InvocationTrackingOverrideSupplier tracking = new(new EntryPointOnlyOverrideSupplier(entryPoint));
        using AsmRunScope scope = scenario.Prepare();
        Action entryFallback = () => AsmFixtureRunner.RunWithOverride(scope, tracking).Dispose();
        entryFallback.Should().Throw<XunitException>().WithMessage("*fell back to the interpreter*");

        ApplicationExecutionObserver observer =
            scope.Observations.Get<ApplicationExecutionObserver>(AsmRunScope.InterpreterExecutionObservation);
        EmulatorCodeInventory inventory =
            scope.Observations.Get<EmulatorCodeInventory>(AsmRunScope.EmulatorCodeInventoryObservation);
        List<SegmentedAddress> applicationAddresses =
            [.. observer.Executions
                .Where(execution => !inventory.IsEmulatorProvided(execution.Address, execution.InstructionBytes))
                .Select(execution => execution.Address)];
        int entryIndex = applicationAddresses.IndexOf(entryPoint);
        entryIndex.Should().BeGreaterThanOrEqualTo(0, "the entry point itself must have been interpreted");
        applicationAddresses.Count.Should().BeGreaterThan(entryIndex + 1,
            "the entry fallback must interpret at least one application instruction after the entry point");
        SegmentedAddress interiorInstruction = applicationAddresses[entryIndex + 1];
        interiorInstruction.Should().NotBe(entryPoint, "the entry point has a successor instruction");
        return interiorInstruction;
    }

    /// <summary>Writes the single-instruction child program the EXEC regression loads, inside the scope's
    /// own working directory so its preparation disposes of it.</summary>
    private static string ChildHaltProgramIn(AsmRunScope scope) {
        string workingDirectory = scope.Fixture.Settings.CDrive
            ?? throw new InvalidOperationException("the EXEC scenario must mount its working directory as C:");
        string childProgram = Path.Join(workingDirectory, ChildProgramFileName);
        File.WriteAllBytes(childProgram, [0xF4]);
        return childProgram;
    }

    /// <summary>Runs the scenario in the interpreter with its own oracle and requires that oracle to fail.</summary>
    private static void AssertInterpretedRunFailsWith(AsmScenario scenario, string messagePattern) {
        using AsmRunScope scope = scenario.Prepare();
        Action interpretedRun = () => AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly).Dispose();
        interpretedRun.Should().Throw<XunitException>().WithMessage(messagePattern);
    }

    /// <summary>Discovery of the correct scenario followed by compilation of the generated source, the
    /// shared first half of every wrong-expectation test. The caller owns the returned override.</summary>
    private static CompiledGeneratedOverride DiscoverAndCompile(AsmScenario discoveryScenario,
        GeneratedPipelineStages stages) {
        GeneratedCodeMachineTestRunner runner = new();
        using AsmRunScope discovery = discoveryScenario.Prepare();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        stages.DiscoveryCompleted = true;
        CompiledGeneratedOverride compiledOverride =
            runner.CompileGeneratedProgram(discovery.Fixture.Settings, generatedProgram);
        stages.CompilationCompleted = true;
        return compiledOverride;
    }

    /// <summary>The compiled execution of a prepared scenario with an already compiled override, the
    /// shared second half of every wrong-expectation test.</summary>
    private static void RunCompiledExecution(AsmScenario executionScenario,
        CompiledGeneratedOverride compiledOverride, GeneratedPipelineStages stages) {
        using AsmRunScope execution = executionScenario.Prepare();
        using (AsmFixtureRunner.RunWithOverride(execution, compiledOverride.Supplier)) {
        }
        stages.CompiledExecutionCompleted = true;
    }

    private static string CsIp(SegmentedAddress address) {
        return $"{address.Segment:X4}:{address.Offset:X4}";
    }

    /// <summary>Reads the bytes of the longest possible instruction at that address, translated the way
    /// the CPU translates it.</summary>
    /// <param name="memory">Memory bus to read from.</param>
    /// <param name="address">Address to read at.</param>
    /// <returns>Up to <see cref="MaxInstructionBytes"/> bytes.</returns>
    private static byte[] ReadMemoryBytes(IMemory memory, SegmentedAddress address) {
        byte[] bytes = new byte[MaxInstructionBytes];
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] = memory.SneakilyRead(MemoryUtils.ToPhysicalAddress(address.Segment, (ushort)(address.Offset + i)));
        }
        return bytes;
    }

    /// <summary>Registers one override at an address the program never calls.</summary>
    private sealed class NeverCalledOverrideSupplier : IOverrideSupplier {
        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            SegmentedAddress address = new(0xF000, 0x1234);
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [address] = new FunctionInformation(address, "never_called", _ => () => { })
            };
        }
    }

    /// <summary>Registers an override at the program entry point whose delegate does nothing, so control
    /// returns to the interpreter at the entry point and the whole application body is interpreted.
    /// The hand-back is implicit: a hand-written override is permissive by default, and this stub exists
    /// to reach the guard, not to satisfy coverage.
    /// The generated-run guard must reject that run.</summary>
    private sealed class EntryPointOnlyOverrideSupplier : IOverrideSupplier {
        private readonly SegmentedAddress _entryPoint;

        public EntryPointOnlyOverrideSupplier(SegmentedAddress entryPoint) {
            _entryPoint = entryPoint;
        }

        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [_entryPoint] = new FunctionInformation(_entryPoint, "entry_point_stub", _ => () => { })
            };
        }
    }

    /// <summary>Registers an override at the program entry point that moves the machine to a later
    /// application instruction, so the interpreter runs that instruction without ever revisiting the
    /// entry point. This is a hand-written partial override that deliberately hands control back to the
    /// interpreter, which its permissive registration allows.</summary>
    private sealed class RedirectFromEntryOverrideSupplier : IOverrideSupplier {
        private readonly SegmentedAddress _entryPoint;
        private readonly SegmentedAddress _interiorInstruction;

        public RedirectFromEntryOverrideSupplier(SegmentedAddress entryPoint, SegmentedAddress interiorInstruction) {
            _entryPoint = entryPoint;
            _interiorInstruction = interiorInstruction;
        }

        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [_entryPoint] = new FunctionInformation(_entryPoint, "entry_redirect_stub", _ => () => {
                    machine.CpuState.CS = _interiorInstruction.Segment;
                    machine.CpuState.IP = _interiorInstruction.Offset;
                })
            };
        }
    }

    /// <summary>Registers one override on the emulator-provided INT 21h callback instead of on any
    /// application address, so the application itself stays entirely interpreted. The empty delegate hands
    /// control back to the interpreter, as a permissive hand-written override does.</summary>
    private sealed class NonApplicationCallbackOverrideSupplier : IOverrideSupplier {
        public bool WasInstalled { get; private set; }

        public SegmentedAddress InstalledAddress { get; private set; }

        /// <summary>Bytes the emulator holds at <see cref="InstalledAddress"/>, read when the supplier ran.</summary>
        public byte[] InstalledBytes { get; private set; } = [];

        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            SegmentedAddress int21Handler = machine.InterruptVectorTable[0x21];
            InstalledAddress = int21Handler;
            InstalledBytes = ReadMemoryBytes(machine.Memory, int21Handler);
            WasInstalled = true;
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [int21Handler] = new FunctionInformation(int21Handler, "int21_emulator_callback_stub", _ => () => { })
            };
        }
    }

    /// <summary>Registers one override at the parent entry point which loads an EXEC child through DOS and
    /// leaves the machine at the child entry, so the parent delegate runs while the child is interpreted.
    /// This is a hand-written partial override that deliberately hands control back to the interpreter,
    /// which its permissive registration allows.</summary>
    private sealed class ExecChildFallbackOverrideSupplier : IOverrideSupplier {
        private readonly SegmentedAddress _parentEntry;
        private readonly string _childProgramPath;

        public ExecChildFallbackOverrideSupplier(SegmentedAddress parentEntry, string childProgramPath) {
            _parentEntry = parentEntry;
            _childProgramPath = childProgramPath;
        }

        /// <summary>Entry of the loaded child. Valid only after the override ran.</summary>
        public SegmentedAddress ChildEntry { get; private set; }

        /// <summary>Why the child could not be prepared for execution, or null when it was.</summary>
        public string? ChildLoadError { get; private set; }

        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [_parentEntry] = new FunctionInformation(_parentEntry, "exec_child_fallback_stub",
                    _ => () => LoadChildAndStayThere(machine))
            };
        }

        private void LoadChildAndStayThere(Machine machine) {
            ushort parentPsp = machine.Dos.DosSwappableDataArea.CurrentProgramSegmentPrefix;
            ushort childFriendlyParentSize = (ushort)(DosProgramSegmentPrefix.PspSizeInParagraphs + 0x10);
            DosErrorCode shrinkResult = machine.Dos.MemoryManager.TryModifyBlock(
                parentPsp, childFriendlyParentSize, out DosMemoryControlBlock _);
            if (shrinkResult != DosErrorCode.NoError) {
                ChildLoadError = $"shrinking the parent block failed with {shrinkResult}";
                return;
            }
            DosExecParameterBlock parameterBlock =
                new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
            DosExecResult result = machine.Dos.ProcessManager.LoadOrLoadAndExecute(
                _childProgramPath, parameterBlock, string.Empty, DosExecLoadType.LoadAndExecute, 0);
            if (!result.Success) {
                ChildLoadError = $"the EXEC failed with {result.ErrorCode}";
                return;
            }
            ChildEntry = new SegmentedAddress(result.InitialCS, result.InitialIP);
        }
    }

    /// <summary>Registers an entry-point override and keeps only a weak reference to the machine, so a test
    /// can prove that a rejected run left none of its emulator graph reachable. The empty delegate hands
    /// control back to the interpreter, as a permissive hand-written override does.</summary>
    private sealed class MachineObservingOverrideSupplier : IOverrideSupplier {
        private readonly SegmentedAddress _entryPoint;

        public MachineObservingOverrideSupplier(SegmentedAddress entryPoint) {
            _entryPoint = entryPoint;
        }

        /// <summary>The machine the override ran on, held weakly. Null until the supplier is asked for its overrides.</summary>
        public WeakReference? MachineReference { get; private set; }

        public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
            ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
            MachineReference = new WeakReference(machine);
            return new Dictionary<SegmentedAddress, FunctionInformation> {
                [_entryPoint] = new FunctionInformation(_entryPoint, "entry_point_stub", _ => () => { })
            };
        }
    }

    /// <summary>How far the shared discovery, compilation and compiled-execution sequence got.</summary>
    private sealed class GeneratedPipelineStages {
        public bool DiscoveryCompleted { get; set; }

        public bool CompilationCompleted { get; set; }

        public bool CompiledExecutionCompleted { get; set; }
    }
}
