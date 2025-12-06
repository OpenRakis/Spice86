namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

public class CyclesClock(State cpuState, long cyclesPerSecond) : IEmulatedClock {
    public long CyclesPerSecond { get; set; } = cyclesPerSecond;

    public double CurrentTimeMs => (double)cpuState.Cycles * 1000 / CyclesPerSecond;
}