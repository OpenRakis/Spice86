namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Promotes a speculative <see cref="CfgInstruction"/> to observed status in place.
///
/// <para>Promotion flips <see cref="CfgInstruction.IsSpeculative"/> to <c>false</c>, compiles the
/// node, installs its SMC write-breakpoints via <see cref="CurrentInstructions.SetAsCurrent"/>, and
/// adds it to <see cref="PreviousInstructions"/> so future hot-cache lookups find it. The node's
/// out-edges (still speculative hints) are left intact; they will be promoted or discarded lazily
/// as execution visits them.</para>
/// </summary>
public class SpeculativePromoter {
    private readonly CfgNodeExecutionCompiler _executionCompiler;
    private readonly CurrentInstructions _currentInstructions;
    private readonly PreviousInstructions _previousInstructions;

    public SpeculativePromoter(CfgNodeExecutionCompiler executionCompiler, CurrentInstructions currentInstructions, PreviousInstructions previousInstructions) {
        _executionCompiler = executionCompiler;
        _currentInstructions = currentInstructions;
        _previousInstructions = previousInstructions;
    }

    /// <summary>
    /// Promotes <paramref name="speculative"/> in place: marks it observed, compiles it, and
    /// registers it in both instruction caches. Returns the same node (now observed).
    /// </summary>
    public CfgInstruction Promote(CfgInstruction speculative) {
        speculative.SetSpeculative(false);
        _executionCompiler.Compile(speculative);
        _currentInstructions.SetAsCurrent(speculative);
        _previousInstructions.AddInstructionInPrevious(speculative);
        return speculative;
    }
}
