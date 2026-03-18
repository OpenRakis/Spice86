namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// A clock based on CPU cycle count, advancing in proportion to a configured cycles-per-second rate.
/// </summary>
public class CyclesClock : ClockBase {
    private readonly State _cpuState;

    public CyclesClock(State cpuState, long cyclesPerSecond, DateTime? startTime = null) {
        _cpuState = cpuState;
        CyclesPerSecond = cyclesPerSecond;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    /// <summary>Gets or sets the number of CPU cycles per second used to calculate elapsed time.</summary>
    public long CyclesPerSecond { get; set; }

    /// <inheritdoc/>
    public override double ElapsedTimeMs => (double)_cpuState.Cycles * 1000 / CyclesPerSecond;
}