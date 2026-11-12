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
internal sealed record ProgramAnalysis(IrSegmentSummaries SegmentSummaries, IrCallingConventions CallingConventions,
    TypedSignatures TypedSignatures, GlobalAccessClassification GlobalClasses) {
    private const int PartCount = 4;

    /// <summary>Runs the program-wide analyses of <paramref name="methods"/> in dependency order:
    /// segment summaries, calling conventions, typed signatures, then the globals
    /// classification over the final segment values. Each analysis is reported as a step of
    /// <paramref name="progress"/> before it starts.</summary>
    public static ProgramAnalysis Compute(IReadOnlyList<PreparedMethod> methods, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder, ProgramImages images, DumpProgressReporter progress) {
        progress.Step(1, PartCount, "segment summaries");
        IrSegmentSummaries summaries = IrSegmentSummaries.Compute(methods, context, effectBuilder);
        progress.Step(2, PartCount, "calling conventions");
        IrCallingConventions conventions = IrCallingConventions.Compute(methods, context,
            round => progress.Step(2, PartCount, $"calling conventions, round {round}"));
        progress.Detail($"Calling conventions fixed point finished in {conventions.Iterations} iterations");
        progress.Step(3, PartCount, "typed signatures");
        TypedSignatures signatures = TypedSignatures.Compute(methods, conventions, context, effectBuilder);
        progress.Step(4, PartCount, "globals classification");
        GlobalAccessClassification classes = GlobalAccessClassification.Build(
            methods.Select(method => (method.IrMethod, summaries.ValuesFor(method.Method), method.Ssa)),
            images);
        return new ProgramAnalysis(summaries, conventions, signatures, classes);
    }
}
