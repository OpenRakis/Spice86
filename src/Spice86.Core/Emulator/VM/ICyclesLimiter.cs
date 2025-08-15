namespace Spice86.Core.Emulator.VM;

/// <summary>
/// This interface provides methods to increase or decrease the CPU cycles per millisecond, as well as a
/// property to  specify the target CPU cycles. It also includes a constant representing the default CPU cycles per
/// millisecond in real mode.
/// </summary>
public interface ICyclesLimiter {
    /// <summary>
    /// The ideal number of CPU cycles for the vast majority of real mode games.
    /// </summary>
    /// <remarks>
    /// Below a value of 8000, Dune music is slowed down.
    /// </remarks>
    public const int RealModeCpuCylcesPerMs = 8000;

    /// <summary>
    /// The current target of CPU cycles to achieve
    /// </summary>
    public int TargetCpuCyclesPerMs { get; set; }

    /// <summary>
    /// Augments the number of target CPU cycles per ms
    /// </summary>
    public void IncreaseCycles();

    /// <summary>
    /// Decreases the number of target CPU cycles per ms
    /// </summary>
    public void DecreaseCycles();
}
