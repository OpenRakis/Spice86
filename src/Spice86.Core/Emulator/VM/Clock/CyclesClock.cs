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

    public double FullIndex => ElapsedTimeMs + _cyclesLimiter.GetCycleProgressionPercentage();

    public DateTime StartTime { get; set; }

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(ElapsedTimeMs);

    public void OnPause() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }

    public void OnResume() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }
}