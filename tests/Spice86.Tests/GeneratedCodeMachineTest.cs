namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Microsoft.Extensions.Logging;

using Spice86.Tests.AsmFixtures;

using System.Buffers.Binary;
using System.Reflection;

using Xunit;

public sealed class GeneratedCodeMachineTest {
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(4, false, false)]
    [InlineData(5, false, false)]
    [InlineData(6, false, false)]
    [InlineData(7, false, false)]
    [InlineData(15, false, false)]
    [InlineData(6, true, false)]
    public void FlashbackVideoPanningGeneratedOverrideMatchesPixels(byte panning, bool splitScreen, bool alternatePage) {
        // The panning inputs are data seeds of the same run, so each row shares the
        // flashback_video machine and generated-source goldens: they stay out of the
        // catalogue, which requires a distinct machine-golden key per registered variant.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.FlashbackVideoPanning(panning, splitScreen, alternatePage));
    }

    // Deletes and re-reads the shared add.spec.generated.cs file that the catalogue "add" case
    // writes concurrently in parallel mode.
    [Fact(DisableParallelization = true)]
    public void AddGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        string outputFile = Path.Join(outputDirectory, "add.spec.generated.cs");
        if (Directory.Exists(outputDirectory)) {
            File.Delete(outputFile);
        }

        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.TestGeneratedCode(AsmFixtureCatalog.Dump("add"));

        File.Exists(outputFile).Should().BeTrue("the generated C# source should be written to the test build output folder");
        File.ReadAllText(outputFile).Should().Be(generatedProgram.SourceText);
    }

    [Fact]
    public void TypedMethodComputesFromItsArgumentsNotTheRegisters() {
        // Arrange: cc_register_leaf's leaf prints as a typed method taking ax and bx and
        // adding them; a direct C# call must compute from the arguments, not from
        // whatever the registers hold when the caller is not generated code.
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("cc_register_leaf"));
        using CompiledGeneratedOverride compiled = CompileLeaf(scope);
        using Spice86Creator creator = new(binName: "cc_register_leaf");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        (Machine machine, object overrides, MethodInfo method) =
            DirectCallHarness(compiled, "Spice86.Generated.CfgGeneratedOverrides", dependencyInjection);

        // Act
        object? result = method.Invoke(overrides, [(ushort)5, (ushort)6]);

        // Assert
        result.Should().Be((ushort)11, "the typed method computes 5 + 6 from its arguments");
        machine.CpuState.AX.Should().Be(11);
        machine.CpuState.IP.Should().Be(0x1234, "the ret pops the pushed return address");
    }

    [Fact]
    public void TypedMethodOverrideComputesFromItsArgumentsNotTheRegisters() {
        // Arrange: the same direct-call contract through a hand-written subclass that
        // overrides the typed method and forwards both arguments to base: the public
        // virtual signature keeps both parameters.
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("cc_register_leaf"));
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(scope);
        List<string> sources = [.. GeneratedOverrideCompiler.TextsOf(generatedProgram),
            """
            namespace Spice86.Generated;

            using Microsoft.Extensions.Logging;
            using Spice86.Core.CLI;
            using Spice86.Core.Emulator.Function;
            using Spice86.Core.Emulator.VM;
            using Spice86.Shared.Emulator.Memory;
            using System.Collections.Generic;

            public sealed class CfgGeneratedOverridesSubclass : CfgGeneratedOverrides {
                public CfgGeneratedOverridesSubclass(
                    IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
                    Machine machine, ILogger loggerService, Configuration configuration,
                    ushort programStartAddress)
                    : base(functionInformations, machine, loggerService, configuration,
                        programStartAddress) {
                }

                public override ushort unknown_F000_00E1_F00E1(ushort ax, ushort bx) =>
                    base.unknown_F000_00E1_F00E1(ax, bx);
            }
            """];
        using CompiledGeneratedOverride compiled =
            new GeneratedOverrideCompiler().CompileSupplier(sources);
        using Spice86Creator creator = new(binName: "cc_register_leaf");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        (Machine machine, object overrides, MethodInfo method) =
            DirectCallHarness(compiled, "Spice86.Generated.CfgGeneratedOverridesSubclass",
                dependencyInjection);

        // Act
        object? result = method.Invoke(overrides, [(ushort)5, (ushort)6]);

        // Assert
        result.Should().Be((ushort)11, "the override computes 5 + 6 from its arguments");
        machine.CpuState.AX.Should().Be(11);
        machine.CpuState.IP.Should().Be(0x1234, "the ret pops the pushed return address");
    }

    [Fact]
    public void TypedMethodsWithoutCopyInsComputeFromTheirArgumentsNotTheRegisters() {
        // Arrange: cc_copy_in_observers keeps a copy-in only where the printed body still
        // observes the machine register. With every register scrambled to values unlike
        // the arguments, a dropped copy-in leaves the scramble where the body reads the
        // register and corrupts the result; the covered reads keep computing from the
        // arguments. SS:SP and the pushed return offset match the other direct calls.
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("cc_copy_in_observers"));
        using CompiledGeneratedOverride compiled = CompileLeaf(scope);
        using Spice86Creator creator = new(binName: "cc_copy_in_observers");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        const string overridesTypeName = "Spice86.Generated.CfgGeneratedOverrides";
        (Machine machine, object overrides, MethodInfo method) = DirectCallHarness(
            compiled, overridesTypeName, dependencyInjection, "unknown_F000_0077_F0077", 0xDEAD);
        machine.CpuState.DS = 0x2000;
        machine.Memory.UInt16[0x2000, 0x0210] = 3;
        machine.Memory.UInt16[0x2000, 0x0200] = 1;
        machine.Memory.UInt16[0x2000, 0x0202] = 2;
        machine.Memory.UInt16[0x2000, 0x0204] = 4;

        // Act + assert S1: phi carried into a loop summing ds:[si] while ds:[210h] decrements.
        // (SP is asserted rather than IP: the ret popping the pushed frame is the
        // same proof and SP is not rewritten by the machine's background scheduler.)
        object? result = method.Invoke(overrides, [(ushort)7, (ushort)0x0200, (ushort)0x2000]);
        result.Should().Be((ushort)14, "s1 sums 7 + 1 + 2 + 4 from its arguments");
        machine.CpuState.SP.Should().Be(0x0100, "the ret pops the pushed return address");

        // Act + assert S2: the untyped call target reads machine AX and DS, and AX after it.
        (machine, overrides, method) = DirectCallHarness(
            compiled, overridesTypeName, dependencyInjection, "unknown_F000_0083_F0083", 0xDEAD);
        machine.CpuState.DS = 0x2000;
        result = method.Invoke(overrides, [(ushort)3, (ushort)0x0089]);
        result.Should().Be((ushort)11, "s2 adds 8 after the untyped call");
        machine.Memory.UInt16[0x2000, 0x0050].Should().Be(3,
            "the untyped callee stores the machine AX the copy-in leaves");
        machine.CpuState.SP.Should().Be(0x0100, "the ret pops the pushed return address");

        // Act + assert S3: a typed return preserves the argument on the above-7 path.
        (machine, overrides, method) = DirectCallHarness(
            compiled, overridesTypeName, dependencyInjection, "unknown_F000_008D_F008D", 0xDEAD);
        result = method.Invoke(overrides, [(ushort)11]);
        result.Should().Be((ushort)11, "s3 returns its argument when it exceeds 7");
        machine.CpuState.SP.Should().Be(0x0100, "the ret pops the pushed return address");

        // Act + assert S4: a partial write keeps the untouched high byte of the argument.
        (machine, overrides, method) = DirectCallHarness(
            compiled, overridesTypeName, dependencyInjection, "unknown_F000_009E_F009E", 0xDEAD);
        method.Invoke(overrides, [(ushort)0xAB00, (ushort)0x2000]);
        machine.Memory.UInt16[0x2000, 0x0070].Should().Be(0xAB00,
            "s4 stores its ax argument");
        machine.Memory.UInt16[0x2000, 0x0074].Should().Be(0xAB05,
            "s4 stores ah carried across the al write");
        machine.CpuState.SP.Should().Be(0x0100, "the ret pops the pushed return address");

        // Act + assert S5: the hidden ds read must reach the argument segment's ds:[80h].
        (machine, overrides, method) = DirectCallHarness(
            compiled, overridesTypeName, dependencyInjection, "unknown_F000_00A7_F00A7", 0xDEAD);
        machine.CpuState.DS = 0x4000;
        method.Invoke(overrides, [(ushort)0x7777, (ushort)0x2000]);
        machine.Memory.UInt16[0x2000, 0x0080].Should().Be(0x7777,
            "s5 stores its ax argument at ds:80h of the argument segment");
        machine.CpuState.SP.Should().Be(0x0100, "the ret pops the pushed return address");
    }

    [Fact]
    public void GeneratingIndependentProgramsConcurrentlyKeepsTheirResults() {
        // Arrange: several independent alu_dead_flags discoveries prepared serially. Generating
        // them concurrently exercises the constant evaluation's ALU scratch state, which must be
        // per evaluation: a shared one lets another generator's flag writes leak into a result.
        const int count = 4;
        AsmRunScope[] scopes = new AsmRunScope[count];
        try {
            CfgPartitionedProgram[] programs = new CfgPartitionedProgram[count];
            for (int i = 0; i < count; i++) {
                scopes[i] = new AsmRunScope(AsmFixtureCatalog.Partition("alu_dead_flags"));
                programs[i] = GeneratedCodeMachineTestRunner.GenerateProgram(scopes[i]).Program;
            }
            GeneratedCSharpProgram[] generated = new GeneratedCSharpProgram[count];
            Parallel.For(0, count,
                i => generated[i] = new CfgCSharpGenerator(collectCensuses: true, verify: true)
                    .Generate(programs[i]));

            // Assert: each concurrently generated program compiles and passes the fixture oracle.
            GeneratedCodeMachineTestRunner runner = new();
            for (int i = 0; i < count; i++) {
                using AsmRunScope execution = new(AsmFixtureCatalog.Partition("alu_dead_flags"));
                using CompiledGeneratedOverride compiled =
                    runner.CompileGeneratedProgram(scopes[i].Fixture.Settings, generated[i]);
                using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(execution, compiled.Supplier);
            }
        } finally {
            foreach (AsmRunScope? scope in scopes) {
                scope?.Dispose();
            }
        }
    }

    /// <summary>The cc_register_leaf leaf's generated program compiled into an override.</summary>
    private static CompiledGeneratedOverride CompileLeaf(AsmRunScope scope) {
        GeneratedCodeMachineTestRunner runner = new();
        return runner.CompileGeneratedProgram(scope.Fixture.Settings,
            runner.GenerateProgramAndSource(scope));
    }

    /// <summary>The <see cref="Machine"/>, overrides instance and leaf method of one direct-call
    /// harness: SS:SP with a pushed return offset, live AX = 3 and BX = 5 distinct from the call's
    /// arguments.</summary>
    private static (Machine Machine, object Overrides, MethodInfo Method) DirectCallHarness(
        CompiledGeneratedOverride compiled, string overridesTypeName,
        Spice86DependencyInjection dependencyInjection,
        string methodName = "unknown_F000_00E1_F00E1", ushort scrambleSeed = 3) {
        Machine machine = dependencyInjection.Machine;
        Type overridesType = compiled.Supplier.GetType().Assembly.GetType(overridesTypeName)
            ?? throw new InvalidOperationException($"{overridesTypeName} missing from the compiled assembly");
        object overrides = Activator.CreateInstance(overridesType,
            new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 }, (ushort)0x170)
            ?? throw new InvalidOperationException($"{overridesTypeName} could not be instantiated");
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.Stack.Push16(0x1234);
        ushort scramble = scrambleSeed;
        machine.CpuState.AX = scramble;
        machine.CpuState.BX = scramble += 2;
        machine.CpuState.CX = scramble += 2;
        machine.CpuState.DX = scramble += 2;
        machine.CpuState.SI = scramble += 2;
        machine.CpuState.DI = scramble += 2;
        machine.CpuState.BP = scramble += 2;
        machine.CpuState.DS = scramble += 2;
        machine.CpuState.ES = (ushort)(scramble + 2);
        MethodInfo method = overridesType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"{methodName} method missing");
        return (machine, overrides, method);
    }

    [Fact]
    public void DataGlobalsGeneratedProgramExposesConstantMemoryAccesses() {
        // Arrange: data_globals reads and writes globals at constant DS offsets in several widths;
        // the collector must surface them on the generated program.
        GeneratedCodeMachineTestRunner runner = new();
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("data_globals"));

        // Act
        GeneratedCSharpProgram program = runner.GenerateProgramAndSource(scope);

        // Assert
        program.ConstantMemoryAccesses.Entries.Should().NotBeEmpty();
    }

    /// <summary>
    /// Discovery observed the IRQ0 handler at F000:0031, so the generated
    /// <c>HardwareInterruptFunction</c> switch arms vector 0x08 only for that address. Pointing the
    /// vector at F000:003C, another generated method the interrupt never delivered to, must fail as
    /// untested at the delivery instead of silently running it.
    /// </summary>
    [Fact]
    public void UnobservedHardwareHandlerDeliveryFailsAsUntested() {
        AsmFixture fixture = AsmFixtureCatalog.InterruptHandlerChangesRegister();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(new AsmRunScope(fixture));
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed handler runs: the program hooks int 8 to F000:0031 itself.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Once the program's own int 8 hook has run, retarget the vector to F000:003C, the generated
        // method of another function discovery never saw an IRQ delivered to. The generated program
        // constant-folds its vector write, so a cycle breakpoint inside the wait loop does it.
        AsmRunSettings retargetedSettings = fixture.Settings with {
            ConfigureMachine = machine => machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                new AddressBreakPoint(BreakPointType.CPU_CYCLES, 1000,
                    (_, _) => machine.InterruptVectorTable[0x08] = new SegmentedAddress(0xF000, 0x003C), true),
                true)
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(retargetedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Hardware interrupt 0x08 targets F000:003C*");
    }

    /// <summary>E4: mouse_irq12 runs discovery with no user routine registered, so the generated
    /// int 74h stub signature-checks the far call's default target. Registering a routine after
    /// discovery makes the driver repoint that call to the routine, which the run must reject as
    /// untested.</summary>
    [Fact]
    public void RuntimeRegisteredMouseRoutineFailsAsUntested() {
        AsmFixture fixture = AsmFixtureCatalog.MouseIrq12NoRoutine();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(new AsmRunScope(fixture));
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed run delivers IRQ 12 with no routine registered and passes.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // A routine registered only at run time repoints the far call's memory pointer to the
        // user routine, which the generated dispatch rejects as an untested target.
        AsmRunSettings registeredSettings = fixture.Settings with {
            ConfigureMachine = machine => {
                machine.MouseDriver.RegisterCallback(
                    new MouseUserCallback(MouseEventMask.Movement, 0xF000, 0x002D));
                machine.DualPic.SetIrqMask(12, false);
                machine.InputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(1, 1));
                machine.InputEventHub.ProcessAllPendingInputEvents();
            }
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(registeredSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Untested far call target F000:002D*");
    }

    /// <summary>One indirect far-call dispatch line arms the shared landing list of every observed target.</summary>
    [Fact]
    public void IndirectFarCallSharesOneLandingLineAcrossTargets() {
        AsmFixture fixture = AsmFixtureCatalog.Partition("partition_far_dispatch");
        GeneratedCSharpProgram generatedProgram = new GeneratedCodeMachineTestRunner().GenerateProgramAndSource(new AsmRunScope(fixture));
        GeneratedSourceFile wiring = generatedProgram.Files.Single(file => file.Name == GeneratedOverrideNames.SupportWiringFileName);

        wiring.Text.Should().Contain("DefineCallLandings(new(biosSegment, 0x0017), [unknown_F000_003D_F003D_AsmEntry, unknown_F000_0041_F0041_AsmEntry, unknown_F000_0045_F0045_AsmEntry, unknown_F000_0047_F0047_AsmEntry], new CallLanding(new(biosSegment, 0x003D), unknown_F000_003D_F003D_AsmEntry), new CallLanding(new(biosSegment, 0x0041), unknown_F000_0041_F0041_AsmEntry));");
        int defineCallLandingsCount = wiring.Text.Split("DefineCallLandings(", StringSplitOptions.None).Length - 1;
        defineCallLandingsCount.Should().Be(1);
    }

    /// <summary>
    /// The int 0x80 call of <c>redirectedreturn</c> was observed landing mid-call at
    /// F000:0057 only, so its landing dispatch arms only that address. Patching the stacked return
    /// IP so the handler's iret lands at F000:0043, another generated method the call never landed
    /// on, must fail as untested instead of running it inside the call like the override chain does.
    /// </summary>
    [Fact]
    public void UnobservedCallLandingFailsAsUntested() {
        AsmFixture fixture = AsmFixtureCatalog.RedirectedReturn();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(new AsmRunScope(fixture));
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed landing runs: the int 80 handler reaches F000:0057 and irets to 0x001D.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Read breakpoints on the two int frames' return-IP slots (SS=0, so the linear address
        // is the offset; int_loop's frame at 0x0FA, int_wrapper's at 0x0F8) rewrite the bumped
        // return 0x0046/0x001D to 0x0043 the moment anything reads it back, so the epilogue's
        // iret lands on F000:0043, a generated entry discovery never observed the int call
        // landing on.
        AsmRunSettings retargetedSettings = fixture.Settings with {
            ConfigureMachine = machine => {
                bool patching = false;
                foreach (long frameIpAddress in new long[] { 0x0FA, 0x0F8 }) {
                    machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                        new AddressBreakPoint(BreakPointType.MEMORY_READ, frameIpAddress,
                            (_, address) => {
                                if (patching) {
                                    return;
                                }
                                patching = true;
                                if (machine.Memory.UInt16[(uint)address] == 0x0046
                                    || machine.Memory.UInt16[(uint)address] == 0x001D) {
                                    machine.Memory.UInt16[(uint)address] = 0x0043;
                                }
                                patching = false;
                            }, false),
                        true);
                }
            }
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(retargetedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*F000:0043*");
    }

    /// <summary>The int 60h of cc_register_leaf's c7_error arm was never delivered during discovery, so
    /// it fails as untested at the int instead of running whatever its vector points at. Writing 0x0200
    /// to the word c7_guard reads (physical 0x20056, always 0 during discovery) makes the run take the
    /// error arm.</summary>
    [Fact]
    public void UnreachedInterruptFailsAsUntested() {
        AsmFixture fixture = AsmFixtureCatalog.Partition("cc_register_leaf");
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(new AsmRunScope(fixture));
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed run keeps [56h] at 0, so c7_guard returns without reaching the int.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Seeding [56h] with 0x0200 makes c7_guard take its error arm and execute the int 60h
        // discovery never delivered.
        AsmRunSettings errorPathSettings = fixture.Settings with {
            ConfigureMachine = machine => machine.Memory.UInt16[0x20056] = 0x0200
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(errorPathSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Untested interrupt at F000:0110*");
    }

    /// <summary>A CPU fault whose handler irets to an address discovery did not observe that fault
    /// resuming at fails as untested instead of running the generated entry there.</summary>
    [Fact]
    public void UnobservedFaultResumeFailsAsUntested() {
        AsmFixture fixture = AsmFixtureCatalog.DivFaultLoop();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(new AsmRunScope(fixture));
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed resume runs: the int 0 handler rewrites the stacked return IP to 0x001D.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // A read breakpoint on the fault frame's return-IP slot (SS=0, so the linear address is
        // the offset; the frame sits at 0x1FA with SP=0x200) rewrites the handler's 0x001D to
        // 0xFFF0 the moment the iret reads it back, so the fault resumes at the generated entry
        // discovery never observed it resuming on.
        AsmRunSettings retargetedSettings = fixture.Settings with {
            ConfigureMachine = machine => {
                bool patching = false;
                machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                    new AddressBreakPoint(BreakPointType.MEMORY_READ, 0x1FA,
                        (_, address) => {
                            if (patching) {
                                return;
                            }
                            patching = true;
                            if (machine.Memory.UInt16[(uint)address] == 0x001D) {
                                machine.Memory.UInt16[(uint)address] = 0xFFF0;
                            }
                            patching = false;
                        }, false),
                    true);
            }
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(retargetedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*F000:FFF0*");
    }

    /// <summary>A terminate whose batch resume lands at an address discovery never observed a batch
    /// launch resuming at fails as untested instead of running the generated entry there.
    /// tsr_batch_continues records the resume into WRITER.COM's COM entry; a batch whose second
    /// program is an EXE resumes at the image's own entry address, which discovery never recorded.
    /// </summary>
    [Fact]
    public void UnobservedBatchLaunchResumeFailsAsUntested() {
        AsmScenario scenario = AsmScenarioCatalog.TsrBatchContinues();
        using AsmRunScope scope = scenario.Prepare();
        AsmFixture fixture = scope.Fixture;
        string resources = Path.Join(AppContext.BaseDirectory, "Resources");

        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(scope);
        using CompiledGeneratedOverride compiledOverride =
            runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // The observed resume runs: WRITER.COM's terminate launches the TSR at its recorded address.
        using (AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier)) {
        }

        // A batch launching an EXE after the TSR resumes at the image's own entry address, which is
        // not the COM :0100 resume discovery recorded.
        using AsmRunScope modifiedScope = new();
        string modifiedDirectory = modifiedScope.CreateWorkingDirectory("tsr_batch_unobserved");
        File.Copy(Path.Join(resources, "DosTsrTests", "tsr_basic.com"),
            Path.Join(modifiedDirectory, "TSR_BASIC.COM"));
        File.Copy(Path.Join(resources, "NativeDosTests", "compiler_idioms.exe"),
            Path.Join(modifiedDirectory, "C.EXE"));
        string modifiedBatch = Path.Join(modifiedDirectory, "START.BAT");
        File.WriteAllText(modifiedBatch, "TSR_BASIC.COM\r\nC.EXE\r\n");
        DosTestFiles.SetFixedTimes(modifiedBatch);
        AsmRunSettings modifiedSettings = fixture.Settings with {
            BinName = modifiedBatch,
            CDrive = modifiedDirectory
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(modifiedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*batch launch*");
    }

    /// <summary>The hardware effect keeps incoming bytes read after partial writes or a branch join.</summary>
    [Fact]
    public void HardwareInterruptSummaryKeepsPartialRegisterInputs() {
        // Arrange
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("interrupt_handler_partial_inputs"));
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(scope).Program;

        // Act
        CallEffect effect = new CfgCSharpGenerator(collectCensuses: true, verify: true)
            .LiftMethods(program)[0].Lowerer.HardwareInterruptEffect;

        // Assert
        CpuValues inputs = CpuValues.Ah | CpuValues.Bh | CpuValues.Ch;
        (effect.Reads & inputs).Should().Be(inputs,
            "the IRQ reads AH after writing AL, BH through a branch join, and CH in JCXZ after writing CL");
    }

    [Fact]
    public void CallStackUnwindGeneratedOverrideUnwindsThroughRecoveryDispatch() {
        // callstackunwind's observed path returns leaf -> recovery -> inner_return, so the unobserved
        // generated execution reaches leaf's speculative non-local return: leaf discards its own return
        // frame and returns straight to inner_return, the address the generated outer call is waiting
        // for. That unwind is raised while the outer call helper is dispatching the recovery override
        // from its recovery loop, so the helper must consume it there and resume at inner_return.
        AsmFixture discovery = AsmFixtureCatalog.CallStackUnwind(SpeculativeArm.Observed);
        AsmFixture unwindExecution = AsmFixtureCatalog.CallStackUnwind(SpeculativeArm.Unobserved);
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(discovery, [discovery, unwindExecution]);
    }

    /// <summary>An <c>int 11h</c> to the installed stub pushes the six-byte interrupt frame in the
    /// interpreter; the generated typed service call omits the frame because the program never
    /// calls DOS 50h or 55h. The program sets known entry flags, seeds the six bytes below SP,
    /// invokes the service and halts with no instruction in between touching flags or the stack.
    /// The interpreted run shows the frame in the seeded slots; the generated run leaves the seeds
    /// untouched, and SP, AX and the flags still match the interpreted run's.</summary>
    [Fact]
    public void TypedServiceCallLeavesTheSeededFrameBytesUntouched() {
        // mov ax,2000h; mov ss,ax; mov sp,1000h; mov bp,sp; push word 0CD7h; popf;
        // six seeded bytes at [bp-6..bp-1]; int 11h; hlt
        byte[] program = [
            0xB8, 0x00, 0x20, 0x8E, 0xD0, 0xBC, 0x00, 0x10, 0x89, 0xE5,
            0x68, 0xD7, 0x0C, 0x9D,
            0xC6, 0x46, 0xFA, 0x11, 0xC6, 0x46, 0xFB, 0x22,
            0xC6, 0x46, 0xFC, 0x33, 0xC6, 0x46, 0xFD, 0x44,
            0xC6, 0x46, 0xFE, 0x55, 0xC6, 0x46, 0xFF, 0x66,
            0xCD, 0x11,
            0xF4
        ];
        const uint frameBase = 0x20000 + 0x1000 - 6;
        ushort returnOffset = (ushort)(0x100 + program.Length - 1);
        ushort entryFlags = new Flags(CpuModel.ZET_86) { FlagRegister16 = 0x0CD7 }.FlagRegister16;

        // The interpreted run leaves the interrupt frame in the seeded slots and provides the
        // discovered CFG the generated run is compiled from.
        byte[] interpretedFrame = new byte[Stack.InterruptFrameSize];
        ushort interpretedAx;
        ushort interpretedFlags;
        ushort callerSegment;
        AsmFinalState emulated;
        CfgPartitionedProgram partitioned;
        using (AsmRunScope interpretedScope = FrameScope(program)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            emulated = AsmFinalState.Capture(interpretedRun, interpretedScope.Fixture.Settings);
            Machine interpreted = interpretedRun.Machine;
            callerSegment = interpreted.CpuState.CS;
            for (int i = 0; i < Stack.InterruptFrameSize; i++) {
                interpretedFrame[i] = interpreted.Memory.UInt8[frameBase + i];
            }
            interpretedAx = interpreted.CpuState.AX;
            interpretedFlags = interpreted.CpuState.Flags.FlagRegister16;
            interpreted.CpuState.SP.Should().Be(0x1000);

            CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(
                interpreted.CfgCpu.ExecutionContextManager, null).Graph;
            GeneratorInputs inputs = interpretedRun.DependencyInjection
                .GeneratorInputsCollector.Capture(interpreted.CfgCpu.ExecutionContextManager);
            partitioned = new CfgFunctionPartitioner().Partition(graph,
                interpreted.CfgCpu.ExecutionContextManager, new FunctionCatalogue(), inputs);
        }

        BinaryPrimitives.ReadUInt16LittleEndian(interpretedFrame.AsSpan(0)).Should().Be(returnOffset,
            "the interpreted int pushed the continuation IP at SP-6");
        BinaryPrimitives.ReadUInt16LittleEndian(interpretedFrame.AsSpan(2)).Should().Be(callerSegment,
            "the interpreted int pushed the caller CS at SP-4");
        BinaryPrimitives.ReadUInt16LittleEndian(interpretedFrame.AsSpan(4)).Should().Be(entryFlags,
            "the interpreted int pushed the entry FLAGS at SP-2");
        interpretedFlags.Should().Be(entryFlags,
            "the service leaves the flags the int entered with");

        // The generated run of the same discovered CFG calls the service directly.
        GeneratedCSharpProgram generated = new CfgCSharpGenerator(
            collectCensuses: false, verify: true).Generate(partitioned);
        generated.SourceText.Should().Contain("ServiceCall(");
        using CompiledGeneratedOverride compiled =
            new GeneratedOverrideCompiler().CompileSupplier(generated);
        using AsmRunScope executionScope = FrameScope(program);
        using AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunWithOverride(executionScope, compiled.Supplier);
        Machine generatedMachine = generatedRun.Machine;
        byte[] generatedFrame = new byte[Stack.InterruptFrameSize];
        for (int i = 0; i < Stack.InterruptFrameSize; i++) {
            generatedFrame[i] = generatedMachine.Memory.UInt8[frameBase + i];
        }
        generatedMachine.CpuState.SP.Should().Be(0x1000);
        generatedMachine.CpuState.AX.Should().Be(interpretedAx,
            "the direct call returns the same AX as the interrupt");
        generatedMachine.CpuState.Flags.FlagRegister16.Should().Be(entryFlags,
            "the direct call leaves the same flags as the interrupt");
        generatedFrame.Should().Equal([0x11, 0x22, 0x33, 0x44, 0x55, 0x66],
            "the frameless typed service call leaves the seeded bytes at SS:SP-6 untouched");
        AsmFinalState generatedState =
            AsmFinalState.Capture(generatedRun, executionScope.Fixture.Settings);
        GeneratedCodeDifferentialReport.Check("service_call_frame.ivt.spec",
            GenerationPolicy.Strict, emulated, generatedState,
            new HashSet<SegmentedAddress>(generated.FramelessServiceCallSites));
    }

    /// <summary>The differential gate excludes only the six bytes a frameless typed call omitted:
    /// when the generated program reads those bytes after the call and stores them to result
    /// memory, the store lands outside the excluded range and the gate must still fail on the
    /// differing RAM bytes - the exclusion must never hide a legitimate read of the frame
    /// area.</summary>
    [Fact]
    public void GeneratedRunReadingOmittedFrameBytesFailsTheDifferentialGate() {
        // mov ax,2000h; mov ss,ax; mov sp,1000h; mov bp,sp; six seeded bytes at [bp-6..bp-1];
        // int 11h; mov al,[bp-4]; mov [0x0200],al; hlt - AL picks up the byte the interpreted
        // int's frame put at SS:SP-4 (caller CS low byte), which the generated run left seeded
        // as 0x33, and the store lands it at DS:0x0200 outside the excluded six bytes.
        byte[] program = [
            0xB8, 0x00, 0x20, 0x8E, 0xD0, 0xBC, 0x00, 0x10, 0x89, 0xE5,
            0xC6, 0x46, 0xFA, 0x11, 0xC6, 0x46, 0xFB, 0x22,
            0xC6, 0x46, 0xFC, 0x33, 0xC6, 0x46, 0xFD, 0x44,
            0xC6, 0x46, 0xFE, 0x55, 0xC6, 0x46, 0xFF, 0x66,
            0xCD, 0x11,
            0x8A, 0x46, 0xFC,
            0xA2, 0x00, 0x02,
            0xF4
        ];

        AsmFinalState emulated;
        uint storeAddress;
        CfgPartitionedProgram partitioned;
        using (AsmRunScope interpretedScope = FrameScope(program)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            emulated = AsmFinalState.Capture(interpretedRun, interpretedScope.Fixture.Settings);
            storeAddress = (uint)interpretedRun.Machine.CpuState.DS * 16 + 0x0200;
            CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(
                interpretedRun.Machine.CfgCpu.ExecutionContextManager, null).Graph;
            GeneratorInputs inputs = interpretedRun.DependencyInjection
                .GeneratorInputsCollector.Capture(interpretedRun.Machine.CfgCpu.ExecutionContextManager);
            partitioned = new CfgFunctionPartitioner().Partition(graph,
                interpretedRun.Machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue(), inputs);
        }

        GeneratedCSharpProgram generated = new CfgCSharpGenerator(
            collectCensuses: false, verify: true).Generate(partitioned);
        generated.SourceText.Should().Contain("ServiceCall(");
        generated.FramelessServiceCallSites.Should().NotBeEmpty(
            "the int 11h in a program without DOS 50h/55h is a frameless typed call");
        AsmFinalState generatedState;
        using (CompiledGeneratedOverride compiled =
            new GeneratedOverrideCompiler().CompileSupplier(generated))
        using (AsmRunScope executionScope = FrameScope(program))
        using (AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunWithOverride(executionScope, compiled.Supplier)) {
            generatedState = AsmFinalState.Capture(generatedRun, executionScope.Fixture.Settings);
        }

        Action check = () => GeneratedCodeDifferentialReport.Check("frame_read.ivt.spec",
            GenerationPolicy.Strict, emulated, generatedState,
            new HashSet<SegmentedAddress>(generated.FramelessServiceCallSites));
        check.Should().Throw<Xunit.Sdk.XunitException>()
            .WithMessage($"*ram:*0x{storeAddress:X5}-0x{storeAddress:X5}*",
                "reading the omitted frame bytes and storing them makes that RAM byte differ " +
                "and the exclusion of the six frame bytes must not hide it");
    }

    /// <summary>Under <c>AbandonedStackSlotsUnobserved</c> the differential gate excludes only the
    /// argument bytes a <c>CallWithStackSpace</c> site reserved without writing: when the program
    /// reads the abandoned argument slot through an address the stack analysis cannot relate to SS
    /// (a segment loaded from memory) and stores the byte to result memory, the store lands outside
    /// the excluded range and the gate must still fail on the differing RAM byte - the exclusion
    /// must never hide a legitimate read of the abandoned area.</summary>
    [Fact]
    public void GeneratedRunReadingAbandonedArgumentSlotFailsTheDifferentialGate() {
        // mov ax,2000h; mov ss,ax; mov sp,1000h; mov word [ss:0300],2000h; push 1234h; call callee;
        // add sp,2; mov ds,[ss:0300]; mov [0x0204],dx; mov al,[0FFEh]; mov [0x0200],al; hlt - DS
        // comes from memory, so the analysis cannot prove the [0FFE] read reaches the abandoned
        // argument slot at SS:0FFE, and the byte lands at DS:0x0200 outside the excluded range.
        // The DX store keeps the callee's argument read an observed output, so the callee is typed.
        // callee: push bp; mov bp,sp; mov dx,[bp+4]; pop bp; ret
        byte[] program = [
            0xB8, 0x00, 0x20, 0x8E, 0xD0, 0xBC, 0x00, 0x10,
            0x36, 0xC7, 0x06, 0x00, 0x03, 0x00, 0x20,
            0x68, 0x34, 0x12,
            0xE8, 0x13, 0x00,
            0x83, 0xC4, 0x02,
            0x36, 0x8E, 0x1E, 0x00, 0x03,
            0x89, 0x16, 0x04, 0x02,
            0xA0, 0xFE, 0x0F,
            0xA2, 0x00, 0x02,
            0xF4,
            0x55, 0x89, 0xE5, 0x8B, 0x56, 0x04, 0x5D, 0xC3
        ];

        (AsmFinalState emulated, GeneratedCSharpProgram generated, AsmFinalState generatedState) =
            InterpretedAndGeneratedStates(program, "abandoned_argument", new GenerationPolicy(
                GenerationRelaxation.AbandonedStackSlotsUnobserved));
        generated.StackSpaceCallSites.Should().NotBeEmpty(
            "under .stk the arg push folds into a stack-space reservation at the call");

        Action check = () => GeneratedCodeDifferentialReport.Check("abandoned_argument.stk",
            new GenerationPolicy(GenerationRelaxation.AbandonedStackSlotsUnobserved),
            emulated, generatedState, new HashSet<SegmentedAddress>(), generated.StackSpaceCallSites,
            generated.DroppedStackSlotSites, FinalStateComparison.Strict);
        check.Should().Throw<Xunit.Sdk.XunitException>()
            .WithMessage("*ram:*0x20200-0x20200*",
                "reading the abandoned argument bytes and storing them makes that RAM byte " +
                "differ and the exclusion of the argument range must not hide it");
    }

    /// <summary>Under <c>AbandonedStackSlotsUnobserved</c> the differential gate excludes only the
    /// bytes a dropped private <c>push bp</c> / <c>pop bp</c> save omitted: when the program reads
    /// the saved-BP slot through an address the stack analysis cannot relate to SS and stores the
    /// byte to result memory, the gate must still fail on the differing RAM byte.</summary>
    [Fact]
    public void GeneratedRunReadingDroppedSavedSlotFailsTheDifferentialGate() {
        // mov ax,2000h; mov ss,ax; mov sp,1000h; mov bp,0BEEFh; mov word [ss:0300],2000h;
        // call callee; mov ds,[ss:0300]; mov [0x0204],dx; mov al,[0FFCh]; mov [0x0200],al; hlt -
        // the callee's dropped push bp wrote 0xBEEF at SS:0FFC in the interpreted run and nothing
        // in the generated one; the byte lands at DS:0x0200 outside the excluded range. The DX
        // store keeps the callee typed so its private save pair can drop.
        // callee: push bp; mov bp,sp; mov dx,1111h; pop bp; ret
        byte[] program = [
            0xB8, 0x00, 0x20, 0x8E, 0xD0, 0xBC, 0x00, 0x10,
            0xBD, 0xEF, 0xBE,
            0x36, 0xC7, 0x06, 0x00, 0x03, 0x00, 0x20,
            0xE8, 0x10, 0x00,
            0x36, 0x8E, 0x1E, 0x00, 0x03,
            0x89, 0x16, 0x04, 0x02,
            0xA0, 0xFC, 0x0F,
            0xA2, 0x00, 0x02,
            0xF4,
            0x55, 0x89, 0xE5, 0xBA, 0x11, 0x11, 0x5D, 0xC3
        ];

        (AsmFinalState emulated, GeneratedCSharpProgram generated, AsmFinalState generatedState) =
            InterpretedAndGeneratedStates(program, "dropped_saved_slot", new GenerationPolicy(
                GenerationRelaxation.AbandonedStackSlotsUnobserved));
        generated.DroppedStackSlotSites.Should().NotBeEmpty(
            "under .stk the callee's private push bp / pop bp pair drops");

        Action check = () => GeneratedCodeDifferentialReport.Check("dropped_saved_slot.stk",
            new GenerationPolicy(GenerationRelaxation.AbandonedStackSlotsUnobserved),
            emulated, generatedState, new HashSet<SegmentedAddress>(), generated.StackSpaceCallSites,
            generated.DroppedStackSlotSites, FinalStateComparison.Strict);
        check.Should().Throw<Xunit.Sdk.XunitException>()
            .WithMessage("*ram:*0x20200-0x20200*",
                "reading the slot the dropped save pair omitted makes that RAM byte differ " +
                "and the exclusion of the saved range must not hide it");
    }

    /// <summary>The recorded <c>NOFILE.TXT</c> literal of the <c>data_strings</c> open call is
    /// checked at run time: bytes that changed since discovery throw instead of silently opening
    /// another name, whether a filename byte or the terminator changed.</summary>
    [Theory]
    [InlineData((ushort)0x0179)]
    [InlineData((ushort)0x0183)]
    public void DataStringsCheckedLiteralFailsOnMutatedBytes(ushort offset) {
        // Arrange
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case("data_strings.ivt.spec");
        using AsmRunScope discovery = scenarioCase.DiscoveryScenario.Prepare();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        generatedProgram.SourceText.Should().Contain("GetDosString(DS, 0x0179, '\\0', \"NOFILE.TXT\")");
        using CompiledGeneratedOverride compiled =
            runner.CompileGeneratedProgram(discovery.Fixture.Settings, generatedProgram);

        AsmScenario mutated = scenarioCase.DiscoveryScenario.WithOracle(fixture =>
            new AsmFixture(fixture.Settings with {
                ConfigureMachine = machine => {
                    fixture.Settings.ConfigureMachine(machine);
                    // Corrupt one byte of the filename or its terminator in the loaded image.
                    machine.Memory.UInt8[machine.CpuState.DS, offset] = (byte)'X';
                }
            }, fixture.ExpectedMemory, fixture.AssertResult));
        using AsmRunScope execution = mutated.Prepare();

        // Act / Assert
        Action run = () => AsmFixtureRunner.RunWithOverride(execution, compiled.Supplier).Dispose();
        run.Should().Throw<InvalidVMOperationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*differs from the recorded literal*");
    }

    /// <summary>Runs a hand-assembled program interpreted, partitions the discovered CFG, generates
    /// the override under <paramref name="policy"/> and runs it: the three states and the generated
    /// program a differential-gate check needs.</summary>
    private static (AsmFinalState Emulated, GeneratedCSharpProgram Generated, AsmFinalState GeneratedState)
        InterpretedAndGeneratedStates(byte[] program, string name, GenerationPolicy policy) {
        AsmFinalState emulated;
        CfgPartitionedProgram partitioned;
        using (AsmRunScope interpretedScope = ProgramScope(program, name)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            emulated = AsmFinalState.Capture(interpretedRun, interpretedScope.Fixture.Settings);
            CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(
                interpretedRun.Machine.CfgCpu.ExecutionContextManager, null).Graph;
            GeneratorInputs inputs = interpretedRun.DependencyInjection
                .GeneratorInputsCollector.Capture(interpretedRun.Machine.CfgCpu.ExecutionContextManager);
            partitioned = new CfgFunctionPartitioner().Partition(graph,
                interpretedRun.Machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue(), inputs);
        }

        GeneratedCSharpProgram generated = new CfgCSharpGenerator(
            new GeneratorOptions { Verify = true, Policy = policy }).Generate(partitioned);
        AsmFinalState generatedState;
        using (CompiledGeneratedOverride compiled =
            new GeneratedOverrideCompiler().CompileSupplier(generated))
        using (AsmRunScope executionScope = ProgramScope(program, name))
        using (AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunWithOverride(executionScope, compiled.Supplier)) {
            generatedState = AsmFinalState.Capture(generatedRun, executionScope.Fixture.Settings);
        }
        return (emulated, generated, generatedState);
    }

    private static AsmRunScope ProgramScope(byte[] program, string name) {
        AsmRunScope scope = new();
        string directory = scope.CreateWorkingDirectory(name);
        string comPath = Path.Join(directory, "program.com");
        File.WriteAllBytes(comPath, program);
        AsmFixtures.DosTestFiles.SetFixedTimes(comPath);
        scope.AttachFixture(new AsmFixture(new AsmRunSettings {
            BinName = comPath,
            MaxCycles = 100000,
            InstallInterruptVectors = true
        }, [], _ => { }));
        return scope;
    }

    private static AsmRunScope FrameScope(byte[] program) {
        AsmRunScope scope = new();
        string directory = scope.CreateWorkingDirectory("service_call_frame");
        string comPath = Path.Join(directory, "frame.com");
        File.WriteAllBytes(comPath, program);
        AsmFixtures.DosTestFiles.SetFixedTimes(comPath);
        scope.AttachFixture(new AsmFixture(new AsmRunSettings {
            BinName = comPath,
            MaxCycles = 100000,
            InstallInterruptVectors = true
        }, [], _ => { }));
        return scope;
    }
}
