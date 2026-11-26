namespace Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// Receives the outcome of every branch probe that generated code runs. Set it on
/// <see cref="Spice86.Core.Emulator.VM.Machine.BranchProbeObserver"/>.
/// </summary>
/// <remarks>
/// Only generated code produced with the branch probe option of the generator (the check build) contains
/// probes. Each probe has a site number, and the manifest written next to the generated sources tells the
/// method, the kind of statement, the printed condition and the address of the instruction that decides each
/// site, or that the site is synthetic. The observer runs on the emulation thread and must not change the
/// machine.
/// </remarks>
public interface IBranchProbeObserver {
    /// <summary>
    /// Called when the condition of an <c>if</c>, a loop or a conditional expression was evaluated.
    /// </summary>
    /// <param name="site">The site number of the probe.</param>
    /// <param name="taken">The value of the condition.</param>
    void OnBranch(int site, bool taken);

    /// <summary>
    /// Called when the value of a <c>switch</c> was evaluated.
    /// </summary>
    /// <param name="site">The site number of the probe.</param>
    /// <param name="value">The value, truncated to 32 bits.</param>
    void OnSelector(int site, uint value);
}
