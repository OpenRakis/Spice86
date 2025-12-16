namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

public class CyclesClock : IEmulatedClock {
    private readonly State _cpuState;

    public CyclesClock(State cpuState, long cyclesPerSecond, DateTime? startTime = null) {
        _cpuState = cpuState;
        CyclesPerSecond = cyclesPerSecond;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    public long CyclesPerSecond { get; set; }

    public double CurrentTimeMs => (double)_cpuState.Cycles * 1000 / CyclesPerSecond;

    public DateTime StartTime { get; set; }

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(CurrentTimeMs);

    public void OnPause() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }

    public void OnResume() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }
}