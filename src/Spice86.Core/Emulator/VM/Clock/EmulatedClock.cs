namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
/// A cycle-based clock that derives time from the CPU cycle limiter's tick counter,
/// matching DOSBox Staging's PIC_Ticks + PIC_TickIndex() model.
/// Reference: DOSBox src/hardware/pic.h PIC_FullIndex() = PIC_Ticks + PIC_TickIndex()
/// Each tick represents 1 millisecond of emulated time.
/// </summary>
public class EmulatedClock : IEmulatedClock {
    private readonly ICyclesLimiter _cyclesLimiter;

    public EmulatedClock(ICyclesLimiter cyclesLimiter, DateTime? startTime = null) {
        _cyclesLimiter = cyclesLimiter;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the elapsed time in whole milliseconds, derived from the cycle limiter's tick count.
    /// Equivalent to DOSBox's PIC_Ticks (number of completed millisecond ticks).
    /// Reference: DOSBox src/hardware/pic.h - extern uint32_t PIC_Ticks
    /// </summary>
    public double ElapsedTimeMs => _cyclesLimiter.TickCount;

    /// <summary>
    /// Gets the full index with sub-millisecond precision from cycle progression.
    /// Equivalent to DOSBox's PIC_FullIndex() = PIC_Ticks + PIC_TickIndex().
    /// Reference: DOSBox src/hardware/pic.h PIC_FullIndex()
    /// </summary>
    public double FullIndex => _cyclesLimiter.TickCount + _cyclesLimiter.GetCycleProgressionPercentage();

    /// <summary>
    /// Thread-safe snapshot of FullIndex, updated atomically by the emulation thread.
    /// Equivalent to DOSBox's PIC_AtomicIndex().
    /// Reference: DOSBox src/hardware/pic.h PIC_AtomicIndex()
    /// </summary>
    public double AtomicFullIndex => _cyclesLimiter.AtomicFullIndex;

    public DateTime StartTime { get; set; }

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(_cyclesLimiter.TickCount);

    /// <inheritdoc/>
    public long ConvertTimeToCycles(double scheduledTime) {
        int cpuCyclesPerMs = _cyclesLimiter.TickCycleMax;
        if (cpuCyclesPerMs == 0) {
            return 0;
        }
        long tickStart = _cyclesLimiter.NextTickBoundaryCycles - cpuCyclesPerMs;
        return tickStart + (long)((scheduledTime - _cyclesLimiter.TickCount) * cpuCyclesPerMs);
    }

    /// <summary>
    /// No-op: tick counting naturally stops when the CPU is paused because
    /// RegulateCycles() is not called and TickCount does not advance.
    /// The limiter's own OnPause handles its internal Stopwatch.
    /// </summary>
    public void OnPause() {
    }

    /// <summary>
    /// No-op: tick counting resumes automatically when the CPU resumes.
    /// </summary>
    public void OnResume() {
    }
}