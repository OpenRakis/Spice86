namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>The IR passes of <see cref="IrMethodEmitter"/>, in the order they run.</summary>
internal enum IrPass {
    PlainTemporaryPropagation,
    InstructionIdioms,
    FlagPropagation,
    Constants,
    InterruptServiceCalls,
    BranchSimplification,
    IfConversion,
    JumpThreading,
    RepCompareLowering,
    AluLowering,
    DeadFlagWrites,
    StoreForwarding,
    DeadRegisterWrites,
    ExpressionPropagation,
    AdditiveConstantFold,
    DeviceOperations,
    DoublePrecision,
    SpeculativeGuardMerge
}

/// <summary>What a partial or full <see cref="IrPassPipeline"/> run produced.</summary>
/// <param name="Ssa">The last SSA the run built and still holds; null when the run stopped before the
/// first build (before <see cref="IrPass.FlagPropagation"/>) or a pass invalidated it by removing
/// reachable blocks (<see cref="IrPass.IfConversion"/>).</param>
/// <param name="Webs">The webs of the SSA as it was before <see cref="IrPass.DeadRegisterWrites"/>,
/// read by the web census; null when the run stopped before it or was not asked to build webs.</param>
/// <param name="DosCalls">The <c>int 21h</c> sites <see cref="DosCallComments.Record"/> found,
/// keyed by instruction origin; empty when the run stopped before <see cref="IrPass.Constants"/>.</param>
internal sealed record IrPipelineResult(IrSsa? Ssa, IrSsaWebs? Webs,
    IReadOnlyDictionary<ICfgNode, DosCallRecord> DosCalls);

/// <summary>
/// The pass sequence <see cref="IrMethodEmitter"/> runs on a lifted method, runnable up to any pass
/// so a test can stop the pipeline right after the pass it checks. The two SSA builds are part of
/// the sequence: the first before <see cref="IrPass.FlagPropagation"/>, the second before
/// <see cref="IrPass.RepCompareLowering"/> (branch simplification and jump threading changed the
/// block structure), plus a rebuild after <see cref="IrPass.InterruptServiceCalls"/> when it
/// typed a call: the typed call writes less than the untyped one, so a second
/// <see cref="IrPass.Constants"/> run on the new SSA serves fresher facts to
/// <see cref="IrPass.BranchSimplification"/>. Between them, <see cref="IrPass.IfConversion"/> folds an
/// instruction-internal two-arm value choice into one conditional assignment so
/// <see cref="IrPass.JumpThreading"/> can remove the emptied join blocks. In verify mode the method is verified with <see cref="IrVerifier.Verify"/> and,
/// when the run reached an SSA build, <see cref="IrVerifier.VerifySsaAfterPasses"/>.
/// </summary>
internal static class IrPassPipeline {
    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/>, building the webs and timing nothing the caller can read.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify) =>
        Run(method, last, verify, true, GeneratorInputs.Empty.IoPorts, new GenerationStepTimer());

    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/>, with <paramref name="ioPorts"/> the port-to-handler-type map of
    /// the machine the program was dumped from; passes that recognize device port sequences match
    /// against it.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify,
        IReadOnlyDictionary<ushort, Type> ioPorts) =>
        Run(method, last, verify, true, ioPorts, new GenerationStepTimer());

    /// <summary>Runs the whole pipeline on <paramref name="method"/>, measuring each pass, each SSA
    /// build, the web build and the verification into <paramref name="timer"/>. The result always has
    /// an SSA, and webs when <paramref name="buildWebs"/> is true; a missing either is a bug and
    /// throws.</summary>
    public static IrFullPipelineResult RunAll(IrMethod method, bool verify, bool buildWebs,
        GenerationStepTimer timer) =>
        RunAll(method, verify, buildWebs, GeneratorInputs.Empty.IoPorts, timer);

    /// <summary>Runs the whole pipeline on <paramref name="method"/> with
    /// <paramref name="ioPorts"/> the port-to-handler-type map of the machine the program was
    /// dumped from, measuring each pass, each SSA build, the web build and the verification into
    /// <paramref name="timer"/>. The result always has an SSA, and webs when
    /// <paramref name="buildWebs"/> is true; a missing either is a bug and throws.</summary>
    public static IrFullPipelineResult RunAll(IrMethod method, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts, GenerationStepTimer timer) {
        IrPipelineResult result = Run(method, IrPass.SpeculativeGuardMerge, verify, buildWebs, ioPorts, timer);
        if (result.Ssa is not { } ssa || (buildWebs && result.Webs is null)) {
            throw new InvalidOperationException(
                $"A full IR pipeline run of {method.Name} must produce an SSA and webs.");
        }
        return new IrFullPipelineResult(ssa, result.Webs, result.DosCalls);
    }

    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/>, measuring each pass, each SSA build, the web build when
    /// <paramref name="buildWebs"/> is true, and the verification into <paramref name="timer"/>.
    /// Without a machine the port map is empty and no device sequence combines.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        GenerationStepTimer timer) =>
        Run(method, last, verify, buildWebs, GeneratorInputs.Empty.IoPorts, timer);

    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/> with <paramref name="ioPorts"/> the port-to-handler-type map of
    /// the machine the program was dumped from, measuring each pass, each SSA build, the web build
    /// when <paramref name="buildWebs"/> is true, and the verification into
    /// <paramref name="timer"/>.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts, GenerationStepTimer timer) {
        IrSsa? ssa = null;
        IrSsaWebs? webs = null;
        IReadOnlyDictionary<ICfgNode, DosCallRecord> dosCalls = new Dictionary<ICfgNode, DosCallRecord>();

        timer.Measure(IrPass.PlainTemporaryPropagation.ToString(), () => IrPlainTemporaryPropagation.Apply(method));
        if (last == IrPass.PlainTemporaryPropagation) {
            return Finish();
        }
        timer.Measure(IrPass.InstructionIdioms.ToString(), () => IrInstructionIdioms.Apply(method));
        if (last == IrPass.InstructionIdioms) {
            return Finish();
        }
        IrSsa ssa1 = timer.Measure("ssa build 1", () => BuildSsa(method));
        ssa = ssa1;
        timer.Measure(IrPass.FlagPropagation.ToString(), () => IrFlagPropagation.Apply(method, ssa1));
        if (last == IrPass.FlagPropagation) {
            return Finish();
        }
        IrConstantFacts facts = timer.Measure(IrPass.Constants.ToString(),
            () => IrConstants.Apply(method, ssa1, method.ToStructuringGraph()));
        dosCalls = DosCallComments.Record(method, facts);
        if (last == IrPass.Constants) {
            return Finish();
        }
        int typedCalls = timer.Measure(IrPass.InterruptServiceCalls.ToString(),
            () => IrInterruptServiceCalls.Apply(method, ssa1, facts));
        if (typedCalls != 0) {
            IrSsa ssaTyped = timer.Measure("ssa build typed calls", () => BuildSsa(method));
            ssa = ssaTyped;
            facts = timer.Measure(IrPass.Constants.ToString(),
                () => IrConstants.Apply(method, ssaTyped, method.ToStructuringGraph()));
        }
        if (last == IrPass.InterruptServiceCalls) {
            return Finish();
        }
        timer.Measure(IrPass.BranchSimplification.ToString(),
            () => IrBranchSimplification.Apply(method, facts));
        if (last == IrPass.BranchSimplification) {
            return Finish();
        }
        int convertedBranches = timer.Measure(IrPass.IfConversion.ToString(),
            () => IrIfConversion.Apply(method));
        if (convertedBranches != 0) {
            // A converted branch removed reachable blocks: the phis and sites of the first SSA no
            // longer describe the method, so a run stopping before the second build has no SSA to verify.
            ssa = null;
        }
        if (last == IrPass.IfConversion) {
            return Finish();
        }
        timer.Measure(IrPass.JumpThreading.ToString(), () => IrJumpThreading.Apply(method));
        if (last == IrPass.JumpThreading) {
            return Finish();
        }
        IrSsa ssa2 = timer.Measure("ssa build 2", () => BuildSsa(method));
        ssa = ssa2;
        timer.Measure(IrPass.RepCompareLowering.ToString(), () => IrRepCompareLowering.Apply(method, ssa2));
        if (last == IrPass.RepCompareLowering) {
            return Finish();
        }
        timer.Measure(IrPass.AluLowering.ToString(), () => IrAluLowering.Apply(method, ssa2));
        if (last == IrPass.AluLowering) {
            return Finish();
        }
        timer.Measure(IrPass.DeadFlagWrites.ToString(), () => IrDeadFlagWrites.Apply(method, ssa2));
        if (last == IrPass.DeadFlagWrites) {
            return Finish();
        }
        timer.Measure(IrPass.StoreForwarding.ToString(), () => IrStoreForwarding.Apply(method, ssa2));
        if (last == IrPass.StoreForwarding) {
            return Finish();
        }
        // Group 2 made new constants (lowered ALU calls, forwarded stores): propagate them on the
        // SSA the passes above kept current before dead writes see their sources.
        timer.Measure(IrPass.Constants + " (group 2)",
            () => IrConstants.Apply(method, ssa2, method.ToStructuringGraph()));
        if (buildWebs) {
            webs = timer.Measure("ssa webs", () => IrSsaWebs.Build(ssa2));
        }
        timer.Measure(IrPass.DeadRegisterWrites.ToString(), () => IrDeadRegisterWrites.Apply(method, ssa2));
        if (last == IrPass.DeadRegisterWrites) {
            return Finish();
        }
        timer.Measure(IrPass.ExpressionPropagation.ToString(), () => IrExpressionPropagation.Apply(method, ssa2));
        if (last == IrPass.ExpressionPropagation) {
            return Finish();
        }
        timer.Measure(IrPass.AdditiveConstantFold.ToString(), () => IrConstantFolder.FoldAdditiveConstants(method, ssa2));
        if (last == IrPass.AdditiveConstantFold) {
            return Finish();
        }
        int combinedSequences = timer.Measure(IrPass.DeviceOperations.ToString(),
            () => IrDeviceOperations.Apply(method, ssa2, ioPorts));
        if (combinedSequences != 0) {
            // Combined statements changed the SSA's reads and writes: rebuild it and its webs for
            // the passes, the census and the verify that follow.
            ssa2 = timer.Measure("ssa build device operations", () => BuildSsa(method));
            ssa = ssa2;
            if (buildWebs) {
                webs = timer.Measure("ssa webs", () => IrSsaWebs.Build(ssa2));
            }
        }
        if (last == IrPass.DeviceOperations) {
            return Finish();
        }
        int rewrittenPairs = timer.Measure(IrPass.DoublePrecision.ToString(),
            () => IrDoublePrecision.Apply(method));
        if (rewrittenPairs != 0) {
            // Rewritten statements made the SSA and its webs stale: rebuild both for the pass,
            // the census and the verify that follow.
            ssa2 = timer.Measure("ssa build double precision", () => BuildSsa(method));
            ssa = ssa2;
            if (buildWebs) {
                webs = timer.Measure("ssa webs", () => IrSsaWebs.Build(ssa2));
            }
        }
        if (last == IrPass.DoublePrecision) {
            return Finish();
        }
        timer.Measure(IrPass.SpeculativeGuardMerge.ToString(), () => IrSpeculativeGuardMerge.Apply(method, ssa2));
        return Finish();

        IrPipelineResult Finish() {
            if (verify) {
                timer.Measure("verify", () => {
                    IrVerifier.Verify(method);
                    if (ssa is not null) {
                        IrVerifier.VerifySsaAfterPasses(method, ssa);
                    }
                });
            }
            return new IrPipelineResult(ssa, webs, dosCalls);
        }
    }

    /// <summary>The SSA of <paramref name="method"/> on a block graph, dominator tree and dominance
    /// frontiers built once for the current block structure.</summary>
    private static IrSsa BuildSsa(IrMethod method) {
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DominatorTree<IrBlock> dominators = DominatorTreeBuilder.BuildFromEntries(graph);
        return IrSsa.Build(graph, dominators, DominanceFrontiers.Compute(graph, dominators));
    }
}
