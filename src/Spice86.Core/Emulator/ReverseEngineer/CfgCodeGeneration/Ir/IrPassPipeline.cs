namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

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
    OverwrittenStores,
    DeadRegisterWrites,
    ExpressionPropagation,
    ConstantChainFold,
    WideComparisons,
    DeviceOperations,
    DoublePrecision,
    JoinedMemory,
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
/// when the run reached an SSA build, <see cref="IrVerifier.VerifySsaMatchesFreshBuild"/>.
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
        IReadOnlyDictionary<ushort, Type> ioPorts, GenerationStepTimer timer) =>
        RunAll(method, verify, buildWebs, ioPorts, _ => false, false, false, [], GenerationPolicy.Strict, timer);

    /// <summary>Runs the whole pipeline on <paramref name="method"/> with
    /// <paramref name="ioPorts"/> and <paramref name="hasOnlyBuiltInTargets"/> the
    /// built-in-targets check <see cref="IrPass.InterruptServiceCalls"/> gates direct service
    /// calls on, measuring each pass, each SSA build, the web build and the verification into
    /// <paramref name="timer"/>. The result always has an SSA, and webs when
    /// <paramref name="buildWebs"/> is true; a missing either is a bug and throws.</summary>
    public static IrFullPipelineResult RunAll(IrMethod method, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets, bool programSetCurrentPsp,
        bool terminateResumedBatchLaunch, IReadOnlyList<SegmentedAddress> execChildEntries,
        GenerationPolicy policy, GenerationStepTimer timer) {
        IrPipelineResult result = Run(method, IrPass.SpeculativeGuardMerge, verify, buildWebs,
            ioPorts, hasOnlyBuiltInTargets, programSetCurrentPsp, terminateResumedBatchLaunch,
            execChildEntries, policy, timer);
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
    /// <paramref name="timer"/>. Without the whole-program target analysis no <c>int</c> site
    /// becomes a direct service call.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts, GenerationStepTimer timer) =>
        Run(method, last, verify, buildWebs, ioPorts, _ => false, false, false, [], GenerationPolicy.Strict, timer);

    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/> with <paramref name="ioPorts"/> and
    /// <paramref name="hasOnlyBuiltInTargets"/> the built-in-targets check and
    /// <paramref name="programSetCurrentPsp"/> the frame rule
    /// <see cref="IrPass.InterruptServiceCalls"/> gates direct service calls on, measuring each
    /// pass, each SSA build, the web build when <paramref name="buildWebs"/> is true, and the
    /// verification into <paramref name="timer"/>.</summary>
    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/> in strict mode.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets, GenerationStepTimer timer) =>
        Run(method, last, verify, buildWebs, ioPorts, hasOnlyBuiltInTargets, false, false,
            [], GenerationPolicy.Strict, timer);

    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets, bool programSetCurrentPsp,
        bool terminateResumedBatchLaunch, IReadOnlyList<SegmentedAddress> execChildEntries,
        GenerationPolicy policy, GenerationStepTimer timer) {
        IrSsa? ssa = null;
        IrSsaWebs? webs = null;
        IReadOnlyDictionary<ICfgNode, DosCallRecord> dosCalls = new Dictionary<ICfgNode, DosCallRecord>();

        IrEditor editor = IrEditor.WithoutSsa;
        string previousStep = "lift";

        void CheckFresh() {
            if (!verify || ssa is null || ssa.IsStale) {
                return;
            }
            if (ssa.IsLivenessStale) {
                timer.Measure($"ssa liveness after {previousStep}", () => ssa.RecomputeLiveness());
            }
            IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
        }
        void Pass(string name, Action action) {
            timer.Measure(name, action);
            previousStep = name;
            CheckFresh();
        }
        T PassResult<T>(string name, Func<T> action) {
            T result = timer.Measure(name, action);
            previousStep = name;
            CheckFresh();
            return result;
        }
        void MakeCurrent() => MakeCurrentTimed($"ssa build stale after {previousStep}");
        void MakeCurrentTimed(string name) {
            if (ssa is null) {
                return;
            }
            if (ssa.IsStale) {
                ssa = timer.Measure(name, () => BuildSsa(method));
                editor = new IrEditor(ssa, timer);
                if (webs is not null) {
                    RebuildWebs();
                }
            } else if (ssa.IsLivenessStale) {
                timer.Measure($"ssa liveness after {previousStep}", () => ssa.RecomputeLiveness());
            }
        }
        void RebuildWebs() {
            if (!buildWebs || ssa is not { } current) {
                return;
            }
            webs = timer.Measure("ssa webs", () => IrSsaWebs.Build(current));
        }

        Pass(IrPass.PlainTemporaryPropagation.ToString(),
            () => IrPlainTemporaryPropagation.Apply(method, IrEditor.WithoutSsa));
        if (last == IrPass.PlainTemporaryPropagation) {
            return Finish();
        }
        Pass(IrPass.InstructionIdioms.ToString(),
            () => IrInstructionIdioms.Apply(method, IrEditor.WithoutSsa));
        if (last == IrPass.InstructionIdioms) {
            return Finish();
        }
        IrSsa ssa1 = timer.Measure("ssa build 1", () => BuildSsa(method));
        ssa = ssa1;
        editor = new IrEditor(ssa1, timer);
        previousStep = "ssa build 1";
        Pass(IrPass.FlagPropagation.ToString(), () => IrFlagPropagation.Apply(method, editor));
        if (last == IrPass.FlagPropagation) {
            return Finish();
        }
        MakeCurrent();
        IrConstantFacts facts = PassResult(IrPass.Constants.ToString(),
            () => IrConstants.Apply(method, editor, method.ToStructuringGraph()));
        dosCalls = DosCallComments.Record(method, facts);
        if (last == IrPass.Constants) {
            return Finish();
        }
        MakeCurrent();
        int typedCalls = PassResult(IrPass.InterruptServiceCalls.ToString(),
            () => IrInterruptServiceCalls.Apply(method, editor, facts, hasOnlyBuiltInTargets, programSetCurrentPsp, terminateResumedBatchLaunch, execChildEntries));
        if (typedCalls != 0) {
            MakeCurrentTimed("ssa build typed calls");
            MakeCurrent();
            facts = PassResult(IrPass.Constants.ToString(),
                () => IrConstants.Apply(method, editor, method.ToStructuringGraph()));
        }
        if (last == IrPass.InterruptServiceCalls) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.BranchSimplification.ToString(),
            () => IrBranchSimplification.Apply(method, editor, facts));
        if (last == IrPass.BranchSimplification) {
            return Finish();
        }
        MakeCurrent();
        PassResult(IrPass.IfConversion.ToString(), () => IrIfConversion.Apply(method, editor));
        if (last == IrPass.IfConversion) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.JumpThreading.ToString(), () => IrJumpThreading.Apply(method, editor));
        if (last == IrPass.JumpThreading) {
            return Finish();
        }
        MakeCurrentTimed("ssa build 2");
        Pass(IrPass.RepCompareLowering.ToString(), () => IrRepCompareLowering.Apply(method, editor));
        if (last == IrPass.RepCompareLowering) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.AluLowering.ToString(), () => IrAluLowering.Apply(method, editor));
        if (last == IrPass.AluLowering) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.DeadFlagWrites.ToString(), () => IrDeadFlagWrites.Apply(method, editor));
        if (last == IrPass.DeadFlagWrites) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.StoreForwarding.ToString(),
            () => IrStoreForwarding.Apply(method, editor, policy));
        if (last == IrPass.StoreForwarding) {
            return Finish();
        }
        MakeCurrent();
        PassResult(IrPass.OverwrittenStores.ToString(),
            () => IrOverwrittenStores.Apply(method, editor, policy));
        if (last == IrPass.OverwrittenStores) {
            return Finish();
        }
        // Group 2 made new constants (lowered ALU calls, forwarded stores): propagate them on the
        // SSA the passes above kept current before dead writes see their sources.
        MakeCurrent();
        Pass(IrPass.Constants + " (group 2)",
            () => IrConstants.Apply(method, editor, method.ToStructuringGraph()));
        MakeCurrent();
        RebuildWebs();
        Pass(IrPass.DeadRegisterWrites.ToString(), () => IrDeadRegisterWrites.Apply(method, editor));
        if (last == IrPass.DeadRegisterWrites) {
            return Finish();
        }
        MakeCurrent();
        int combinedValues = PassResult("value sequences",
            () => IrInstructionIdioms.ApplyValueSequences(method, editor));
        MakeCurrent();
        if (combinedValues != 0) {
            RebuildWebs();
        }
        MakeCurrent();
        int combinedProducts = PassResult("products",
            () => IrDoublePrecision.ApplyProducts(method, editor));
        MakeCurrent();
        if (combinedProducts != 0) {
            RebuildWebs();
        }
        MakeCurrent();
        Pass(IrPass.ExpressionPropagation.ToString(),
            () => IrExpressionPropagation.Apply(method, editor, policy));
        if (last == IrPass.ExpressionPropagation) {
            return Finish();
        }
        bool removedChainStatements = PassResult(IrPass.ConstantChainFold.ToString(),
            () => IrConstantFolder.FoldConstantChains(method, editor));
        MakeCurrent();
        if (removedChainStatements) {
            RebuildWebs();
        }
        if (last == IrPass.ConstantChainFold) {
            return Finish();
        }
        int foldedComparisons = PassResult(IrPass.WideComparisons.ToString(),
            () => IrWideComparisons.Apply(method, editor));
        MakeCurrent();
        if (foldedComparisons != 0) {
            RebuildWebs();
        }
        if (last == IrPass.WideComparisons) {
            return Finish();
        }
        int combinedSequences = PassResult(IrPass.DeviceOperations.ToString(),
            () => IrDeviceOperations.Apply(method, editor, ioPorts));
        MakeCurrent();
        if (combinedSequences != 0) {
            RebuildWebs();
        }
        if (last == IrPass.DeviceOperations) {
            return Finish();
        }
        int rewrittenPairs = PassResult(IrPass.DoublePrecision.ToString(),
            () => IrDoublePrecision.Apply(method, editor));
        MakeCurrent();
        if (rewrittenPairs != 0) {
            RebuildWebs();
        }
        if (last == IrPass.DoublePrecision) {
            return Finish();
        }
        int joinedAccesses = PassResult(IrPass.JoinedMemory.ToString(),
            () => IrJoinedMemory.Apply(method, editor, policy));
        MakeCurrent();
        if (joinedAccesses != 0) {
            RebuildWebs();
        }
        if (last == IrPass.JoinedMemory) {
            return Finish();
        }
        MakeCurrent();
        Pass(IrPass.SpeculativeGuardMerge.ToString(),
            () => IrSpeculativeGuardMerge.Apply(method, editor, policy));
        return Finish();

        IrPipelineResult Finish() {
            MakeCurrent();
            if (verify) {
                timer.Measure("verify", () => {
                    IrVerifier.Verify(method);
                    if (ssa is not null) {
                        IrVerifier.VerifySsa(method, ssa);
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
