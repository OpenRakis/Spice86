namespace Spice86.Tests;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Reflection;
using System.Runtime.Loader;

using Xunit;

public sealed class GeneratedCodeMachineTest {
    [Fact]
    public void AlignedReturnTransfersReturnHelperAction() {
        GeneratedCodeMachineTestRunner runner = new();
        (CfgPartitionedProgram program, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("partition_shared_tail", maxCycles: 1000);

        program.Transfers.Should().Contain(transfer => transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
        generatedProgram.SourceText.Should().Contain("return NearRet(");
    }

    [Fact]
    public void CpuFaultTransfersUseDedicatedFaultLowering() {
        GeneratedCodeMachineTestRunner runner = new();
        (CfgPartitionedProgram program, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("divfaultloop", maxCycles: 1000);

        program.Transfers.Should().Contain(transfer => transfer.Kind == CfgCodePartitionTransferKind.CpuFault);
        // CPU-fault transfers enter the handler partition directly: by the transfer point the fault-specific
        // work (push flags/return address, clear InterruptFlag, set CS/IP) already ran in the catch block, so
        // entering the handler is a normal partition entry (no CpuFaultTransfer wrapper).
        generatedProgram.SourceText.Should().NotContain("CpuFaultTransfer");
    }

    [Fact]
    public void AddGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("add", maxCycles: 1000);
    }

    [Fact]
    public void GenerateProgramAndSourceWritesGeneratedSourceToBuildFolder() {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        string outputFile = Path.Join(outputDirectory, "add.generated.cs");
        if (Directory.Exists(outputDirectory)) {
            File.Delete(outputFile);
        }

        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("add", maxCycles: 1000);

        File.Exists(outputFile).Should().BeTrue("the generated C# source should be written to the test build output folder");
        File.ReadAllText(outputFile).Should().Be(generatedProgram.SourceText);
    }

    [Fact]
    public void Jump1GeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("jump1", maxCycles: 1000);
    }

    [Fact]
    public void AlwaysTakenConditionalJumpGuardsUnobservedFallthroughWithIf() {
        // jump1 contains conditional jumps that were always taken during discovery (e.g. `clc; ja j01`),
        // so their fallthrough (next-in-memory) edge was never observed. With speculation off, each such
        // jump must lower to an `if` that guards the unobserved fallthrough with FailAsUntested, not
        // silently collapse to an unconditional transfer to the taken target. (The speculation-on
        // resolution of the same fallthroughs is covered by SpeculationOnJump1ResolvesUnobservedFallthroughs.)
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = false
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("jump1", options);

        generatedProgram.SourceText.Should().Contain("Unobserved conditional fallthrough");
    }

    [Fact]
    public void UnobservedConditionalArmToDiscoveredSamePartitionBlockLowersToGoto() {
        // jump2 contains `F000:12A2 jcxz short 0x129D`: the taken arm to 0x129D was never observed during
        // discovery (CX was never zero there), but 0x129D is a block entry discovered through another path in
        // the same partition. The generator must synthesize that edge and emit a plain `goto` to the existing
        // label rather than failing as untested: only the edge is untested, not the target instruction.
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("jump2", maxCycles: 10000);
        string source = generatedProgram.SourceText;

        source.Should().Contain("// F000:12A2 jcxz short 0x129D");
        source.Should().MatchRegex(@"goto label_F000_129D_F129D_\d+;");
        source.Should().NotContain("Unobserved conditional jump target at F000:12A2");
    }


    [Fact]
    public void MethodEndingInTerminatorEmitsNoTrailingUntestedThrow() {
        // A method whose last emitted node diverges (ret/hlt/goto/partition-return) can never reach the
        // trailing "reached the end without a terminating control-flow instruction" throw, so completion
        // analysis must suppress that dead safety net. add ends in a terminator, so no method body in its
        // generated source should carry the trailing throw.
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("add", maxCycles: 1000);

        generatedProgram.SourceText.Should().NotContain(
            "Generated partition reached the end without a terminating control-flow instruction.");
    }

    [Fact]
    public void PartitionSharedTailGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("partition_shared_tail", maxCycles: 1000);
    }

    [Fact]
    public void PartitionIndirectCallJumpGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("partition_indirect_call_jump", maxCycles: 1000);
    }

    [Fact]
    public void PartitionMutualRecursionUnwindGeneratedOverrideCompilesAndRuns() {
        // partition_mutual_recursion_unwind has two call-target partitions that jump to each other in a
        // strongly connected component, lowered as CyclicCrossPartitionFlow (JumpDispatcher.Jump +
        // RequiredJumpAsmReturn). The first call bounces a -> b -> a, re-entering function_a while it is
        // still on the jump stack: the mutual-recursion unwind where JumpAsmReturn is read while still null.
        // This crashed with InvalidOperationException before RequiredJumpAsmReturn returned a no-op there.
        byte[] expected = new byte[0x12];
        expected[0x10] = 0xAA; // function_a_done marker
        expected[0x11] = 0xBB; // function_b_done marker
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("partition_mutual_recursion_unwind", expected, maxCycles: 10000);
    }

    [Fact]
    public void DivFaultLoopGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("divfaultloop", [0x03, 0x00, 0x02, 0x00], maxCycles: 1000);
    }

    [Fact]
    public void InterruptGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("interrupt", maxCycles: 10000);
    }

    [Fact]
    public void SelfModifyJeGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("selfmodifyje", maxCycles: 10000);
    }

    [Fact]
    public void SelfModifyTerminatorGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("selfmodifyterminator", maxCycles: 10000);
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
        byte[] expected = new byte[0x13];
        expected[0x10] = 0xAA; // variant A marker
        expected[0x11] = 0xBB; // variant B marker
        expected[0x12] = 0xCC; // variant C marker
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("selfmodifycalltarget", expected, maxCycles: 20000);
    }

    [Fact]
    public void SelfModifyingCallTargetSharedAddressEmitsUniqueNamesAndSingleRegistration() {
        // The same fixture asserted at the source level: the shared call-target address yields several
        // partitions, but the generated source must declare distinct method names for them and register the
        // address exactly once (the override catalogue is keyed by address).
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("selfmodifycalltarget", maxCycles: 20000);
        string source = generatedProgram.SourceText;

        System.Text.RegularExpressions.Regex.Matches(source, @"DefineFunction\(cs1, 0x0028,").Count
            .Should().Be(1, "the address-keyed override catalogue must be registered exactly once per address");
    }

    [Theory]
    [InlineData("selfmodifycall")]
    [InlineData("partition_cross_function_loop")]
    [InlineData("partition_jump_into_function_middle")]
    [InlineData("partition_multi_entry_dominated_shared")]
    [InlineData("partition_multi_entry_irreducible_shared")]
    [InlineData("partition_mixed_activation_cycle")]
    [InlineData("returnedterminator")]
    public void AdditionalPartitionGeneratedOverridesCompileAndMatchMachineTestOracle(string binName) {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(binName, maxCycles: 10000);
    }

    [Fact]
    public void IoPortInstructionsLowerThroughIoPortDispatcher() {
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("jmpmov", maxCycles: 10000);

        // IN/OUT execution-AST helper calls must reach the I/O bus through Machine.IoPortDispatcher,
        // not through non-existent In8/Out16 members of CSharpOverrideHelper.
        generatedProgram.SourceText.Should().Contain("Machine.IoPortDispatcher.WriteWord(");
        generatedProgram.SourceText.Should().NotContain("Out16(");
    }

    [Fact]
    public void FarJumpAndFarCallGeneratedOverridesCompileAndRun() {
        // jmpmov exercises a runtime far-jump dispatch; jump2 exercises a runtime far-call dispatch.
        // Both must transfer explicitly on a matched observed target instead of falling through to the
        // trailing untested-target failure.
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("jmpmov", maxCycles: 10000);
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("jump2", maxCycles: 10000);
    }

    [Fact]
    public void CallWithoutObservedContinuationFailsAsUntestedOnReturn() {
        // segpr contains a direct call whose callee never returns during discovery, so there is no observed
        // continuation edge. The generator must still emit the call helper with the statically-known expected
        // return address, then guard the unobserved post-call path with an explicit untested failure.
        GeneratedCodeMachineTestRunner runner = new();
        (CfgPartitionedProgram program, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("segpr", maxCycles: 10000);

        program.Transfers.Should().Contain(transfer => transfer.Kind == CfgCodePartitionTransferKind.CallOut);
        generatedProgram.SourceText.Should().Contain("but no continuation was observed during discovery.");
        runner.TestGeneratedCode("segpr", maxCycles: 10000);
    }

    [Fact]
    public void WideSegmentSpanProgramInitializesSegmentFieldsFromObservedConstants() {
        // intchain.com is loaded near the DOS PSP but reaches BIOS code at segment 0xF000, so its observed
        // generated-code segments span both relocatable program code and the fixed-address BIOS. Relocation is
        // out of scope: every segment field must be initialized directly to its observed constant, so the
        // fixed-address 0xF000 segment is reproduced exactly regardless of any runtime entry segment.
        string comFileName = Path.GetFullPath("Resources/cpuTests/intchain.com");
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource(
            comFileName, maxCycles: 1000, installInterruptVectors: true);
        string source = generatedProgram.SourceText;

        // The fixed-address BIOS segment is emitted as a direct constant assignment, not a relocated value.
        source.Should().Contain("= 0xF000;");
        // The relocation machinery is gone from generated output.
        source.Should().NotContain("GetRelocationBaseSegment");
        source.Should().NotContain("relocationBaseSegment");
        source.Should().NotContain("relocated from the runtime entry segment");
        // The generated source still compiles.
        using CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);
    }

    [Fact]
    public void MultipleMisalignedCallContinuationsGeneratedOverrideCompilesAndRuns() {
        // multimisalignedcall has a single shared CALL node whose callee discards its real return address
        // and returns to a different continuation on each run (an overlay/trampoline thunk). None of those
        // continuations is the instruction statically following the CALL, so the call node accumulates
        // several CallToMisalignedReturn successors. The generator must not reject that shape: misaligned
        // continuations are resolved at runtime by ExecuteCallEnsuringSameStack, so generation must succeed.
        byte[] expected = new byte[0x13];
        expected[0x00] = 0x03; // counter reached 3 (three dispatcher runs)
        expected[0x10] = 0xAA; // cont0 marker
        expected[0x11] = 0xBB; // cont1 marker
        expected[0x12] = 0xCC; // cont2 marker
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("multimisalignedcall", expected, maxCycles: 10000);
    }

    [Fact]
    public void PushCsCallNearRetFarGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[2];
        expected[0x00] = 0x42; // AX low byte = 0x42
        expected[0x01] = 0x00; // AX high byte = 0x00
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("pushcs_callnear_retfar", expected, maxCycles: 1000);
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
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(binName, maxCycles: 10000);
    }

    [Fact]
    public void BitwiseGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = GeneratedCodeMachineTestRunner.GetExpectedMemoryDump("bitwise");
        // dosbox values (same divergence from the recorded dump as the emulated-mode test).
        expected[0x9F] = 0x12;
        expected[0x9D] = 0x12;
        expected[0x9B] = 0x12;
        expected[0x99] = 0x12;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("bitwise", expected, maxCycles: 10000);
    }

    [Fact]
    public void ControlGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = GeneratedCodeMachineTestRunner.GetExpectedMemoryDump("control");
        // dosbox value (same divergence from the recorded dump as the emulated-mode test).
        expected[0x1] = 0x78;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("control", expected, maxCycles: 10000);
    }

    [Fact]
    public void MulGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = GeneratedCodeMachineTestRunner.GetExpectedMemoryDump("mul");
        // dosbox values (same divergence from the recorded dump as the emulated-mode test).
        expected[0xA2] = 0x86;
        expected[0x9E] = 0x46;
        expected[0x9C] = 0x87;
        expected[0x9A] = 0x83;
        expected[0x98] = 0x82;
        expected[0x96] = 0x86;
        expected[0x92] = 0x46;
        expected[0x73] = 0x2;
        expected[0xAA] = 0x42;
        expected[0xAE] = 0x2;
        expected[0xB0] = 0x3;
        expected[0xB2] = 0x2;
        expected[0xB4] = 0x3;
        expected[0xB6] = 0x42;
        expected[0xBA] = 0x2;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("mul", expected, maxCycles: 10000);
    }

    [Fact]
    public void Div2GeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[6];
        expected[0x00] = 0x3D; // quotient low  (AX = 0x8F3D)
        expected[0x01] = 0x8F; // quotient high
        expected[0x02] = 0x89; // remainder low (DX = 0x9089)
        expected[0x03] = 0x90; // remainder high
        expected[0x04] = 0xC3; // divisor low   (CX = 0xE4C3)
        expected[0x05] = 0xE4; // divisor high
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("div2", expected, maxCycles: 10000);
    }

    [Fact]
    public void LockPrefixGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("lockprefix", [], new GeneratedCodeRunOptions { MaxCycles = 10000 }, machine => {
            machine.Memory.UInt16[0, 0x0000].Should().Be(3, "three invalid LOCK uses should each trigger INT 6");
            machine.Memory.UInt16[0, 0x0002].Should().Be(3, "three valid LOCK uses should complete without triggering INT 6");
        });
    }

    [Fact]
    public void SelfModifyValueGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[4];
        expected[0x00] = 0x0a;
        expected[0x01] = 0x00;
        expected[0x02] = 0xff;
        expected[0x03] = 0xff;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("selfmodifyvalue", expected, maxCycles: 10000);
    }

    [Fact]
    public void SelfModifyInstructionsGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[6];
        expected[0x00] = 0x03;
        expected[0x01] = 0x00;
        expected[0x02] = 0x02;
        expected[0x03] = 0x00;
        expected[0x04] = 0x01;
        expected[0x05] = 0x00;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("selfmodifyinstructions", expected, maxCycles: 10000);
    }

    [Fact]
    public void ExternalIntGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[6];
        expected[0x00] = 0x01;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("externalint", expected,
            new GeneratedCodeRunOptions { MaxCycles = 0xFFFFFFF, EnablePit = true });
    }

    [Fact]
    public void LinearAddressSameButSegmentedDifferentGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[2];
        expected[0x00] = 0x02;
        expected[0x01] = 0x00;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("linearsamesegmenteddifferent", expected,
            new GeneratedCodeRunOptions { MaxCycles = 100000, EnableA20Gate = true });
    }

    [Fact]
    public void StiCliGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        // Reaching the end of Run without an untested-failure throw confirms the generated body
        // executed STI/CLI and terminated at HLT. (The generated Hlt() helper exits via
        // HaltRequestedException, so CpuState.IsRunning is not the relevant invariant here, unlike the
        // emulated-mode CPU HLT path.)
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("sticli", [], new GeneratedCodeRunOptions { MaxCycles = 1000 });
    }

    [Fact]
    public void SpeculativeBranchGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_branch", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeClosureGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xBB;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_closure", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeConvergenceGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x404];
        expected[0x400] = 0x01;
        expected[0x401] = 0xAA;
        expected[0x402] = 0xCC;
        expected[0x403] = 0xFF;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_convergence", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeInvalidOpcodeGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xEE;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_invalid_opcode", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeCallEntryGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_call_entry", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeSmcGuardGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_smc_guard", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeDiscardGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_discard", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void SpeculativeMixedBlockGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xDD;
        expected[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_mixed_block", expected,
            new GeneratedCodeRunOptions { MaxCycles = 1000, EnableSpeculativeCfgExploration = true });
    }

    [Fact]
    public void Test386ButNotProtectedModeGeneratedOverrideCompilesAndReachesPostFinished() {
        Test386PostPortHandler? handler = null;
        GeneratedCodeRunOptions options = new() {
            MaxCycles = long.MaxValue,
            FailOnUnhandledPort = true,
            ConfigureMachine = machine => {
                handler = new Test386PostPortHandler(machine.CpuState, Substitute.For<ILoggerService>(), machine.IoPortDispatcher);
            }
        };

        new GeneratedCodeMachineTestRunner().TestGeneratedCode("test386", [], options, _ => {
            Test386PostPortHandler postHandler = handler ?? throw new InvalidOperationException("The test386 POST port handler was not installed.");
            postHandler.PostValues.Count.Should().Be(8);
            // FF means test finished normally.
            postHandler.PostValues[^1].Should().Be(0xFF);
        });
    }
}
