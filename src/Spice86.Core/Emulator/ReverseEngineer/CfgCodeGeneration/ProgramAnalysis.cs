namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;

using System.Linq;

/// <summary>
/// What the generator knows about the whole program once every method is prepared: the segment
/// summaries, the calling conventions, the typed signatures and the globals classification, each
/// computed over the prepared methods of the program before any method is analyzed or printed.
/// </summary>
/// <param name="CallingConventions">The code demand: what generated code reads after a call. It
/// drives the printed conventions and the typed signatures.</param>
/// <param name="ObservedConventions">The observed demand: the code demand plus the
/// terminal-observation demand (a machine-leaving exit observes the whole reportable state). It
/// drives only the read sets and write-backs of the round-two exits.</param>
internal sealed record ProgramAnalysis(IrSegmentSummaries SegmentSummaries, IrCallingConventions CallingConventions,
    IrCallingConventions ObservedConventions,
    TypedSignatures TypedSignatures, GlobalAccessClassification GlobalClasses,
    StackArguments StackArguments) {
    private const int PartCount = 6;

    /// <summary>Runs the program-wide analyses of <paramref name="methods"/> in dependency order:
    /// segment summaries, the code and observed conventions, typed signatures, then the globals
    /// classification over the final segment values. Each analysis is reported as a step of
    /// <paramref name="progress"/> before it starts.</summary>
    public static ProgramAnalysis Compute(IReadOnlyList<PreparedMethod> methods, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder, ProgramImages images, DumpProgressReporter progress,
        ParallelOptions parallelOptions) {
        progress.Step(1, PartCount, "segment summaries");
        IrSegmentSummaries summaries = IrSegmentSummaries.Compute(methods, context, effectBuilder);
        Action<int> codeRound = round =>
            progress.Step(2, PartCount, $"calling conventions, round {round}");
        Action<int> observedRound = round =>
            progress.Step(3, PartCount, $"observed conventions, round {round}");
        progress.Step(2, PartCount, "calling conventions");
        progress.Step(3, PartCount, "observed conventions");
        IrCallingConventions[] results = new IrCallingConventions[2];
        Parallel.For(0, 2, parallelOptions,
            i => results[i] = IrCallingConventions.Compute(methods, context, observed: i == 1,
                i == 0 ? codeRound : observedRound, effectBuilder));
        IrCallingConventions conventions = results[0];
        IrCallingConventions observed = results[1];
        progress.Detail($"Calling conventions fixed point finished in {conventions.Iterations} iterations");
        progress.Detail($"Observed conventions fixed point finished in {observed.Iterations} iterations");
        progress.Step(4, PartCount, "typed signatures");
        TypedSignatures signatures = TypedSignatures.Compute(methods, conventions);
        progress.Step(5, PartCount, "globals classification");
        GlobalAccessClassification classes = GlobalAccessClassification.Build(
            methods.Select(method => (method.IrMethod, summaries.ValuesFor(method.Method), method.Ssa)),
            images);
        progress.Step(6, PartCount, "stack arguments");
        StackArguments stackArguments = StackArguments.Compute(methods, signatures,
            context, effectBuilder, images);
        return new ProgramAnalysis(summaries, conventions, observed, signatures, classes,
            stackArguments);
    }
}
