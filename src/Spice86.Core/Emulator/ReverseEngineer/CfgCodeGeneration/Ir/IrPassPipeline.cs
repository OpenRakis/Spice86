namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

/// <summary>The IR passes of <see cref="IrMethodEmitter"/>, in the order they run.</summary>
internal enum IrPass {
    PlainTemporaryPropagation,
    InstructionIdioms,
    FlagPropagation,
    Constants,
    BranchSimplification,
    JumpThreading,
    RepCompareLowering,
    AluLowering,
    DeadFlagWrites,
    DeadRegisterWrites,
    ExpressionPropagation,
    AdditiveConstantFold
}

/// <summary>What a partial or full <see cref="IrPassPipeline"/> run produced.</summary>
/// <param name="Ssa">The last SSA the run built; null when the run stopped before the first
/// build (before <see cref="IrPass.FlagPropagation"/>).</param>
/// <param name="Webs">The webs of the SSA as it was before <see cref="IrPass.DeadRegisterWrites"/>,
/// read by the web census; null when the run stopped before it or was not asked to build webs.</param>
internal sealed record IrPipelineResult(IrSsa? Ssa, IrSsaWebs? Webs);

/// <summary>
/// The pass sequence <see cref="IrMethodEmitter"/> runs on a lifted method, runnable up to any pass
/// so a test can stop the pipeline right after the pass it checks. The two SSA builds are part of
/// the sequence: the first before <see cref="IrPass.FlagPropagation"/>, the second before
/// <see cref="IrPass.RepCompareLowering"/> (branch simplification and jump threading changed the
/// block structure). In verify mode the method is verified with <see cref="IrVerifier.Verify"/> and,
/// when the run reached an SSA build, <see cref="IrVerifier.VerifySsaAfterPasses"/>.
/// </summary>
internal static class IrPassPipeline {
    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/>, building the webs and timing nothing the caller can read.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify) =>
        Run(method, last, verify, true, new GenerationStepTimer());

    /// <summary>Runs the whole pipeline on <paramref name="method"/>, measuring each pass, each SSA
    /// build, the web build and the verification into <paramref name="timer"/>. The result always has
    /// an SSA, and webs when <paramref name="buildWebs"/> is true; a missing either is a bug and
    /// throws.</summary>
    public static IrFullPipelineResult RunAll(IrMethod method, bool verify, bool buildWebs,
        GenerationStepTimer timer) {
        IrPipelineResult result = Run(method, IrPass.AdditiveConstantFold, verify, buildWebs, timer);
        if (result.Ssa is not { } ssa || (buildWebs && result.Webs is null)) {
            throw new InvalidOperationException(
                $"A full IR pipeline run of {method.Name} must produce an SSA and webs.");
        }
        return new IrFullPipelineResult(ssa, result.Webs);
    }

    /// <summary>Runs every pass up to and including <paramref name="last"/> on
    /// <paramref name="method"/>, measuring each pass, each SSA build, the web build when
    /// <paramref name="buildWebs"/> is true, and the verification into <paramref name="timer"/>.</summary>
    public static IrPipelineResult Run(IrMethod method, IrPass last, bool verify, bool buildWebs,
        GenerationStepTimer timer) {
        IrSsa? ssa = null;
        IrSsaWebs? webs = null;

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
        if (last == IrPass.Constants) {
            return Finish();
        }
        timer.Measure(IrPass.BranchSimplification.ToString(),
            () => IrBranchSimplification.Apply(method, facts));
        if (last == IrPass.BranchSimplification) {
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
            return new IrPipelineResult(ssa, webs);
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
