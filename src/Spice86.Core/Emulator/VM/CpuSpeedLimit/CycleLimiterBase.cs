namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Abstract base class for CPU speed limitation strategies.
/// </summary>
public abstract class CycleLimiterBase : ICyclesLimiter {

    /// <inheritdoc />
    public int TargetCpuCyclesPerMs { get; set; }

    /// <summary>
    /// Adjusts CPU cycles to control emulation speed.
    /// </summary>
    /// <param name="cpuState">The CPU state containing cycle information.</param>
    internal abstract void RegulateCycles(State cpuState);
    
    /// <inheritdoc />
    public abstract void DecreaseCycles();
    
    /// <inheritdoc />
    public abstract void IncreaseCycles();
}
