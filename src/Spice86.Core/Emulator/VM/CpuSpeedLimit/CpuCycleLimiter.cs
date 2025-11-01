namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
///     Tracks the configured cycles-per-millisecond target and exposes increase/decrease helpers for UI bindings.
/// </summary>
public class CpuCycleLimiter : ICyclesLimiter {
    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CpuCycleLimiter" /> class.
    /// </summary>
    /// <param name="targetCpuCyclesPerMs">The target CPU cycles per millisecond. A value of 0 is corrected to 3000.</param>
    public CpuCycleLimiter(int targetCpuCyclesPerMs) {
        TargetCpuCyclesPerMs =
            Math.Clamp(targetCpuCyclesPerMs > 0 ? targetCpuCyclesPerMs : ICyclesLimiter.RealModeCpuCyclesPerMs,
                MinCyclesPerMs, MaxCyclesPerMs);
    }

    /// <inheritdoc />
    public int TargetCpuCyclesPerMs { get; set; }

    /// <inheritdoc />
    public void IncreaseCycles() {
        TargetCpuCyclesPerMs = Math.Min(TargetCpuCyclesPerMs + CyclesUp, MaxCyclesPerMs);
    }

    /// <inheritdoc />
    public void DecreaseCycles() {
        TargetCpuCyclesPerMs = Math.Max(TargetCpuCyclesPerMs - CyclesDown, MinCyclesPerMs);
    }
}