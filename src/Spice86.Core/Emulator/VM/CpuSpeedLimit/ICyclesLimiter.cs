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
    /// Also, too much CPU cycles can make emulation performance worse, and sometimes even starves other threads (ie. sound/music gets cut off, UI freezes!)
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
}
