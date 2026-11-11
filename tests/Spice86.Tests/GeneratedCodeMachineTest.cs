namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

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

                public override ushort unknown_F000_00AB_F00AB(ushort ax, ushort bx) =>
                    base.unknown_F000_00AB_F00AB(ax, bx);
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
        Spice86DependencyInjection dependencyInjection) {
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
        machine.CpuState.AX = 3;
        machine.CpuState.BX = 5;
        MethodInfo method = overridesType.GetMethod("unknown_F000_00AB_F00AB")
            ?? throw new InvalidOperationException("unknown_F000_00AB_F00AB method missing");
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

    /// <summary>The one approved frame difference: an <c>int 11h</c> to the installed stub pushes
    /// the six-byte interrupt frame in the interpreter, while the generated direct call to
    /// <c>GetEquipmentList</c> builds none. The program sets known entry flags, seeds the six bytes
    /// below SP, invokes the service and halts with no instruction in between touching flags or the
    /// stack. The interpreted run must show return IP, caller CS and the entry FLAGS in the seeded
    /// slots; the generated run must leave the seeds untouched; SP, AX and the flags must match the
    /// interpreted run's.</summary>
    [Fact]
    public void BuiltInOperationWithoutFrameDependencyLeavesStackBytesUntouched() {
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
        byte[] interpretedFrame = new byte[6];
        ushort interpretedAx;
        ushort interpretedFlags;
        ushort callerSegment;
        CfgPartitionedProgram partitioned;
        using (AsmRunScope interpretedScope = FrameScope(program)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            Machine interpreted = interpretedRun.Machine;
            callerSegment = interpreted.CpuState.CS;
            for (int i = 0; i < 6; i++) {
                interpretedFrame[i] = interpreted.Memory.UInt8[frameBase + i];
            }
            interpretedAx = interpreted.CpuState.AX;
            interpretedFlags = interpreted.CpuState.Flags.FlagRegister16;
            interpreted.CpuState.SP.Should().Be(0x1000);

            CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(
                interpreted.CfgCpu.ExecutionContextManager, null).Graph;
            GeneratorInputs inputs = interpretedRun.DependencyInjection
                .GeneratorInputsCollector.Capture();
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
        generated.SourceText.Should().Contain("GetEquipmentList(");
        using CompiledGeneratedOverride compiled =
            new GeneratedOverrideCompiler().CompileSupplier(generated);
        using AsmRunScope executionScope = FrameScope(program);
        using AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunWithOverride(executionScope, compiled.Supplier);
        Machine generatedMachine = generatedRun.Machine;
        byte[] generatedFrame = new byte[6];
        for (int i = 0; i < 6; i++) {
            generatedFrame[i] = generatedMachine.Memory.UInt8[frameBase + i];
        }
        generatedMachine.CpuState.SP.Should().Be(0x1000);
        generatedMachine.CpuState.AX.Should().Be(interpretedAx,
            "the direct call returns the same AX as the interrupt");
        generatedMachine.CpuState.Flags.FlagRegister16.Should().Be(entryFlags,
            "the direct call leaves the same flags as the interrupt");
        generatedFrame.Should().Equal([0x11, 0x22, 0x33, 0x44, 0x55, 0x66],
            "the direct call builds no interrupt frame: the seeded bytes survive");
    }

    /// <summary>The recorded <c>NOFILE.TXT</c> literal of the <c>data_strings</c> open call is
    /// checked at run time: bytes that changed since discovery throw instead of silently opening
    /// another name, whether a filename byte or the terminator changed.</summary>
    [Theory]
    [InlineData((ushort)0x016F)]
    [InlineData((ushort)0x0179)]
    public void DataStringsCheckedLiteralFailsOnMutatedBytes(ushort offset) {
        // Arrange
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case("data_strings.ivt.spec");
        using AsmRunScope discovery = scenarioCase.DiscoveryScenario.Prepare();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        generatedProgram.SourceText.Should().Contain("GetDosString(DS, 0x016F, '\\0', \"NOFILE.TXT\")");
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

    private static AsmRunScope FrameScope(byte[] program) {
        AsmRunScope scope = new();
        string directory = scope.CreateWorkingDirectory("frame_omission");
        string comPath = Path.Join(directory, "frame.com");
        File.WriteAllBytes(comPath, program);
        scope.AttachFixture(new AsmFixture(new AsmRunSettings {
            BinName = comPath,
            MaxCycles = 100000,
            InstallInterruptVectors = true
        }, [], _ => { }));
        return scope;
    }
}
