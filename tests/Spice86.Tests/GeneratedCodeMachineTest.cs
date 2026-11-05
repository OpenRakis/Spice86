namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using Microsoft.Extensions.Logging;

using Spice86.Tests.AsmFixtures;

using System.Reflection;

using Xunit;

public sealed class GeneratedCodeMachineTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlashbackVideoGeneratedOverrideMatchesPixels(bool unchained) {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.FlashbackVideo(unchained));
    }

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
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.FlashbackVideoPanning(panning, splitScreen, alternatePage));
    }
    [Fact]
    public void TestRomWrite() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.RomWrite());
    }

    [Fact]
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
    public void Jump1GeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Dump("jump1"));
    }

    [Fact]
    public void AlwaysTakenConditionalJumpGuardsUnobservedFallthroughWithIf() {
        // jump1 contains conditional jumps that were always taken during discovery (e.g. `clc; ja j01`),
        // so their fallthrough (next-in-memory) edge was never observed. With speculation off, each such
        // jump must lower to an `if` that guards the unobserved fallthrough with FailAsUntested, not
        // silently collapse to an unconditional transfer to the taken target. The guarded `if` with
        // FailAsUntested is now visible in the `jump1` golden (no suffix); the run checks the `jump1`
        // memory dump.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Jump1WithoutSpeculation());
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

    [Fact]
    public void PartitionSharedTailGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition("partition_shared_tail"));
    }

    [Fact]
    public void PartitionMultiEntryDisconnectedSharedGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        // The shared region's two exits are each reached from both entries and do not reach each other, so
        // the blocks no single entry dominates form two unconnected regions. Partitioning must keep both.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition("partition_multi_entry_disconnected_shared"));
    }

    [Fact]
    public void PartitionIndirectCallJumpGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition("partition_indirect_call_jump"));
    }

    [Fact]
    public void PartitionMutualRecursionUnwindGeneratedOverrideCompilesAndRuns() {
        // partition_mutual_recursion_unwind has two call-target partitions that jump to each other in a
        // strongly connected component, lowered as CyclicCrossPartitionFlow (JumpDispatcher.Jump +
        // RequiredJumpAsmReturn). The first call bounces a -> b -> a, re-entering function_a while it is
        // still on the jump stack: the mutual-recursion unwind where JumpAsmReturn is read while still null.
        // This crashed with InvalidOperationException before RequiredJumpAsmReturn returned a no-op there.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.PartitionMutualRecursionUnwind());
    }

    [Fact]
    public void OwnerlessCallCycleGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.OwnerlessCallCycle());
    }

    [Fact]
    public void SameretLoopGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        // sameretloop's function returns to its own entry address on every iteration, so the dispatcher
        // must recognize each unchanged-CS:IP return as a control transfer and dispatch the override
        // again; the fallback guard rejects any iteration the interpreter would run instead.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SameretLoop());
    }

    [Fact]
    public void DivFaultLoopGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.DivFaultLoop());
    }

    [Fact]
    public void InterruptGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Dump("interrupt"));
    }

    [Fact]
    public void SelfModifyJeGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyJe());
    }

    [Fact]
    public void SelfModifyTerminatorGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyTerminator());
    }

    [Fact]
    public void SelfModifyingCallTargetSharedAddressGeneratedOverrideCompilesAndRuns() {
        // selfmodifycalltarget reproduces the Dune2 "unknown_3409_0025" failure: one CALL-target address
        // is self-modified between calls so several distinct instruction variants live at that one address.
        // The function partitioner promotes each variant into its own partition, all sharing the same entry
        // address. Before the fix this produced several C# methods with the identical name (CS0111) and
        // several DefineFunction registrations at the identical address (an UnrecoverableException when the
        // override is installed). Compiling the generated source and installing the override here proves both
        // are resolved: unique method names and a single registration per address.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyCallTarget());
    }

    [Theory]
    [InlineData("partition_cross_function_loop")]
    [InlineData("partition_jump_into_function_middle")]
    [InlineData("partition_multi_entry_dominated_shared")]
    [InlineData("partition_multi_entry_irreducible_shared")]
    [InlineData("partition_mixed_activation_cycle")]
    public void AdditionalPartitionGeneratedOverridesCompileAndMatchMachineTestOracle(string binName) {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition(binName));
    }

    [Fact]
    public void SelfModifyCallGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyCall());
    }

    [Fact]
    public void ReturnedTerminatorGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.ReturnedTerminator());
    }

    [Fact]
    public void JmpMovGeneratedOverrideCompilesAndRuns() {
        // jmpmov exercises a runtime far-jump dispatch; it must transfer explicitly on a matched
        // observed target instead of falling through to the trailing untested-target failure.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.JmpMov());
    }

    [Fact]
    public void Jump2GeneratedOverrideCompilesAndRuns() {
        // jump2 exercises a runtime far-call dispatch; it must transfer explicitly on a matched
        // observed target instead of falling through to the trailing untested-target failure.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Dump("jump2"));
    }

    [Fact]
    public void CallWithoutObservedContinuationFailsAsUntestedOnReturn() {
        // segpr contains a direct call whose callee never returns during discovery, so there is no observed
        // continuation edge. The generator must still emit the call helper with the statically-known expected
        // return address, then guard the unobserved post-call path with an explicit untested failure.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Dump("segpr"));
    }

    [Fact]
    public void MultipleMisalignedCallContinuationsGeneratedOverrideCompilesAndRuns() {
        // multimisalignedcall has a single shared CALL node whose callee discards its real return address
        // and returns to a different continuation on each run (an overlay/trampoline thunk). The returns
        // are stack-balanced: each ret consumes the call's own frame, so the call node accumulates
        // CallToRedirectedReturn successors. The continuations stay in the caller's method, and the
        // generated code runs them by dispatching to the actual landing address when
        // NearCallReturnedElsewhere reports a redirected return.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.MultiMisalignedCall());
    }

    [Fact]
    public void PushCsCallNearRetFarGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.PushCsCallNearRetFar());
    }

    [Theory]
    [InlineData("bcdcnv")]
    [InlineData("cmpneg")]
    [InlineData("datatrnf")]
    [InlineData("div")]
    [InlineData("rep")]
    [InlineData("rotate")]
    [InlineData("shifts")]
    [InlineData("strings")]
    [InlineData("sub")]
    public void BasicCpuGeneratedOverridesCompileAndMatchMachineTestOracle(string binName) {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Dump(binName));
    }

    [Fact]
    public void BitwiseGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Bitwise());
    }

    [Fact]
    public void ControlGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Control());
    }

    [Fact]
    public void MulGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Mul());
    }

    [Fact]
    public void Div2GeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Div2());
    }

    [Fact]
    public void LockPrefixGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.LockPrefix());
    }

    [Fact]
    public void SelfModifyValueGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyValue());
    }

    [Fact]
    public void SelfModifyInstructionsGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyInstructions());
    }

    [Fact]
    public void SelfModifyRepGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SelfModifyRep());
    }

    [Fact]
    public void ExternalIntGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.ExternalInt());
    }

    [Fact]
    public void StiPendingGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.StiPending());
    }

    [Fact]
    public void IoPortsGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.IoPorts());
    }

    [Fact]
    public void InteriorEntryIrqGeneratedOverrideMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.InteriorEntry());
    }

    [Fact]
    public void InterruptHandlerChangesRegisterGeneratedOverrideMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.InterruptHandlerChangesRegister());
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
    public void RegisterLocalsAcrossGeneratedOverrideMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition("register_locals_across"));
    }

    [Fact]
    public void LinearAddressSameButSegmentedDifferentGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.LinearSameSegmentedDifferent());
    }

    [Fact]
    public void StiCliGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        // The oracle checks the value the program writes: mov [si], ax stores AX at DS:SI.
        // (The generated Hlt() helper exits via HaltRequestedException, so CpuState.IsRunning is not
        // the relevant invariant here, unlike the emulated-mode CPU HLT path.)
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.StiCli());
    }

    [Fact]
    public void SpeculativeCallEntryGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SpeculativeCallEntry());
    }

    [Fact]
    public void SpeculativeInvalidOpcodeGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SpeculativeInvalidOpcode());
    }

    [Fact]
    public void SpeculativeTransferIntoDataGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        // Each never-taken arm ends in a direct jump or call whose target holds invalid opcodes, so the
        // transfer has no successor in the CFG. The generator must fail that transfer as untested at run
        // time instead of throwing while it generates the whole program.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.SpeculativeTransferIntoData());
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

    [Fact]
    public void Test386ButNotProtectedModeGeneratedOverrideCompilesAndReachesPostFinished() {
        GeneratedCodeMachineTestRunner runner = new();
        runner.TestGeneratedCode(AsmFixtureCatalog.Test386());
    }
}
