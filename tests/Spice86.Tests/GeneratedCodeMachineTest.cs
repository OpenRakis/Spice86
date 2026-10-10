namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Tests.AsmFixtures;

using Xunit;

public sealed class GeneratedCodeMachineTest {
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
    public void PartitionSharedTailGeneratedOverrideCompilesAndMatchesMachineTestOracle() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.Partition("partition_shared_tail"));
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
        // and returns to a different continuation on each run (an overlay/trampoline thunk). None of those
        // continuations is the instruction statically following the CALL, so the call node accumulates
        // several CallToMisalignedReturn successors. The generator must not reject that shape: misaligned
        // continuations are resolved at runtime by ExecuteCallEnsuringSameStack, so generation must succeed.
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
    public void Test386ButNotProtectedModeGeneratedOverrideCompilesAndReachesPostFinished() {
        GeneratedCodeMachineTestRunner runner = new();
        runner.TestGeneratedCode(AsmFixtureCatalog.Test386());
    }
}
