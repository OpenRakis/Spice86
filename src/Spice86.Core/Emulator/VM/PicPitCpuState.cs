namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

/// <summary>
///     Execution slice state shared with the NewPIC event queue and PIT scheduler.
/// </summary>
public sealed class PicPitCpuState(State state) {
    /// <summary>
    ///     Delegate executed by the CPU core to decode the next instruction.
    /// </summary>
    /// <returns>Pointer to the entry point for the decoded instruction.</returns>
    public delegate nint CpuDecoder();

    /// <summary>
    ///     Cycles remaining before the PIC/PIT scheduler re-evaluates pending work.
    /// </summary>
    public int Cycles { get; set; }

    /// <summary>
    ///     Remaining cycles in the active slice budget.
    /// </summary>
    public int CyclesLeft { get; set; }

    /// <summary>
    ///     Maximum cycles allocated to the current slice.
    /// </summary>
    public int CyclesMax { get; set; }

    /// <summary>
    ///     Current processor interrupt flag.
    /// </summary>
    public bool InterruptFlag {
        get => state.InterruptFlag;
        set => state.InterruptFlag = value;
    }

    /// <summary>
    ///     Gets a value indicating whether the CPU currently suppresses external interrupts due to shadowing.
    /// </summary>
    public bool InterruptShadowing => state.InterruptShadowing;

    /// <summary>
    ///     Last hardware interrupt vector raised by the controller.
    /// </summary>
    public byte? LastHardwareInterrupt { get; private set; }

    /// <summary>
    ///     Number of cycles consumed inside the active slice.
    /// </summary>
    public int TickIndexNd => CyclesMax - CyclesLeft - Cycles;

    /// <summary>
    ///     Computes the fractional progress through the current slice.
    /// </summary>
    /// <returns>Progress in the range 0.0–1.0 relative to <see cref="CyclesMax" />.</returns>
    public double GetTickIndex() {
        if (CyclesMax == 0) {
            return 0.0;
        }

        return (double)TickIndexNd / CyclesMax;
    }

    /// <summary>
    ///     Converts a normalized slice fraction into an integer cycle count.
    /// </summary>
    /// <param name="amount">Fraction of the slice, typically between 0.0 and 1.0.</param>
    /// <returns>Number of cycles corresponding to the provided fraction.</returns>
    public int MakeCycles(double amount) {
        double cycles = CyclesMax * amount;
        Debug.Assert(cycles is >= int.MinValue and <= int.MaxValue);
        return (int)cycles;
    }

    /// <summary>
    ///     Records the most recent hardware interrupt vector observed by the controller.
    /// </summary>
    /// <param name="num">Interrupt vector number.</param>
    public void CpuHwInterrupt(byte num) {
        LastHardwareInterrupt = num;
    }

    /// <summary>
    ///     Clears the stored hardware interrupt vector.
    /// </summary>
    public void ClearLastHardwareInterrupt() {
        LastHardwareInterrupt = null;
    }
}
