namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

/// <summary>
///     Execution slice state shared with the Device Scheduler event queue and PIT scheduler.
/// </summary>
public sealed class ExecutionStateSlice(State state) {
    /// <summary>
    ///     Delegate executed by the CPU core to decode the next instruction.
    /// </summary>
    /// <returns>Pointer to the entry point for the decoded instruction.</returns>
    public delegate nint CpuDecoder();

    /// <summary>
    ///     Cycles remaining before the Device Scheduler or PIT scheduler re-evaluates pending work.
    /// </summary>
    public int CyclesUntilReevaluation { get; set; }

    /// <summary>
    ///     Remaining cycles in the active slice budget.
    /// </summary>
    public int CyclesLeft { get; set; }

    /// <summary>
    ///     Maximum cycles allocated to the current slice.
    /// </summary>
    public int CyclesAllocated { get; set; }

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
    public int CyclesConsumed => CyclesAllocated - CyclesLeft - CyclesUntilReevaluation;

    /// <summary>
    ///     Computes the fractional progress through the current slice.
    /// </summary>
    /// <returns>Progress in the range 0.0–1.0 relative to <see cref="CyclesAllocated" />.</returns>
    public double NormalizedSliceProgress {
        get {
            if (CyclesAllocated == 0) {
                return 0.0;
            }

            return (double)CyclesConsumed / CyclesAllocated;
        }
    }

    /// <summary>
    ///     Converts a normalized slice fraction into an integer cycle count.
    /// </summary>
    /// <param name="amount">Fraction of the slice, typically between 0.0 and 1.0.</param>
    /// <returns>Number of cycles corresponding to the provided fraction.</returns>
    public int ConvertNormalizedToCycles(double amount) {
        double cycles = CyclesAllocated * amount;
        Debug.Assert(cycles is >= int.MinValue and <= int.MaxValue);
        return (int)cycles;
    }

    /// <summary>
    ///     Records the most recent hardware interrupt vector observed by the controller.
    /// </summary>
    /// <param name="num">Interrupt vector number.</param>
    public void SetLastHardwareInterrupt(byte num) {
        LastHardwareInterrupt = num;
    }

    /// <summary>
    ///     Clears the stored hardware interrupt vector.
    /// </summary>
    public void ClearLastHardwareInterrupt() {
        LastHardwareInterrupt = null;
    }
}
