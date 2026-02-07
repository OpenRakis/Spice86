namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;

public class CyclesClock : IEmulatedClock {
    private readonly State _cpuState;
    private readonly ICyclesLimiter _cyclesLimiter;

    public CyclesClock(State cpuState, ICyclesLimiter cyclesLimiter, long cyclesPerSecond, DateTime? startTime = null) {
        _cpuState = cpuState;
        _cyclesLimiter = cyclesLimiter;
        CyclesPerSecond = cyclesPerSecond;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    public long CyclesPerSecond { get; set; }

    public double ElapsedTimeMs => (double)_cpuState.Cycles * 1000 / CyclesPerSecond;

    /// <summary>
    /// Gets the full index with sub-ms precision from cycle counting.
    /// FullIndex = ElapsedTimeMs already has sub-ms precision from the cycle conversion,
    /// so no additional CycleProgressionPercentage is added (that would double-count).
    /// Equivalent to DOSBox's PIC_FullIndex().
    /// </summary>
    public double FullIndex => ElapsedTimeMs;

    /// <summary>
    /// Thread-safe snapshot of FullIndex, updated atomically by the emulation thread.
    /// Equivalent to DOSBox's PIC_AtomicIndex().
    /// </summary>
    public double AtomicFullIndex => _cyclesLimiter.AtomicFullIndex;

    public DateTime StartTime { get; set; }

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(ElapsedTimeMs);

    public void OnPause() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }

    public void OnResume() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }
}