namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
/// This interface provides methods to increase or decrease CPU speed, for speed sensitive games.
/// </summary>
public interface ICyclesLimiter {
    /// <summary>
    /// The ideal number of CPU cycles for the vast majority of real mode games.
    /// </summary>
    public const int RealModeCpuCyclesPerMs = 3000;

    /// <summary>
    /// The current target of CPU cycles to achieve
    /// </summary>
    public int TargetCpuCyclesPerMs { get; set; }

    /// <summary>
    /// Limits the number of emulated CPU cycles per ms, for speed sensitive games.
    /// </summary>
    /// <remarks>
    /// Also, too many CPU cycles can make emulation performance worse,
    /// and sometimes even starves other threads (ie. sound/music gets cut off, UI freezes!)
    /// </remarks>
    public void RegulateCycles();

    /// <summary>
    /// Augments the number of target CPU cycles per ms
    /// </summary>
    public void IncreaseCycles();

    /// <summary>
    /// Decreases the number of target CPU cycles per ms
    /// </summary>
    public void DecreaseCycles();

    /// <summary>
    /// Gets the number of cycles not done yet (ND) within the current millisecond tick.
    /// Equivalent to DOSBox Staging's PIC_TickIndexND().
    /// </summary>
    /// <returns>The number of cycles not yet completed in the current millisecond tick.</returns>
    public long GetNumberOfCyclesNotDoneYet();

    /// <summary>
    /// Gets the percent of cycles completed within the current millisecond tick of the CPU.
    /// Equivalent to DOSBox Staging's PIC_TickIndex().
    /// </summary>
    /// <returns>A value between 0.0 and 1.0 representing the percentage of cycles completed.</returns>
    public double GetCycleProgressionPercentage();
}
