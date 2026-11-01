namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// What the generator knows about the whole program once every method is prepared: the segment
/// summaries, the calling conventions, the typed signatures and the globals classification, each
/// computed over the prepared methods of the program before any method is analyzed or printed.
/// </summary>
internal sealed record ProgramAnalysis(IrSegmentSummaries SegmentSummaries, IrCallingConventions CallingConventions,
    TypedSignatures TypedSignatures, GlobalAccessClassification GlobalClasses) {
    /// <summary>Runs the program-wide analyses of <paramref name="methods"/> in dependency order:
    /// segment summaries, calling conventions, typed signatures, then the globals
    /// classification over the final segment values.</summary>
    public static ProgramAnalysis Compute(IReadOnlyList<PreparedMethod> methods, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder, ProgramImages images) {
        IrSegmentSummaries summaries = IrSegmentSummaries.Compute(methods, context, effectBuilder);
        IrCallingConventions conventions = IrCallingConventions.Compute(methods, context);
        TypedSignatures signatures = TypedSignatures.Compute(methods, conventions, context, effectBuilder);
        GlobalAccessClassification classes = GlobalAccessClassification.Build(
            methods.Select(method => (method.IrMethod, summaries.ValuesFor(method.Method), method.Ssa)),
            images);
        return new ProgramAnalysis(summaries, conventions, signatures, classes);
    }
}
