namespace Spice86.Emulator.Devices.Timer;

using Spice86.Emulator.CPU;

/// <summary>
/// Counter activator based on emulated cycles
/// </summary>
public class CyclesCounterActivator : ICounterActivator {
    private readonly long _instructionsPerSecond;
    private readonly State _state;
    private long _cyclesBetweenActivations;
    private long _lastActivationCycle;

    public CyclesCounterActivator(State state, long instructionsPerSecond) {
        this._state = state;
        this._instructionsPerSecond = instructionsPerSecond;
    }

    public bool IsActivated() {
        long currentCycles = _state.Cycles;
        long elapsedInstructions = _state.Cycles - _lastActivationCycle;
        if (elapsedInstructions <= _cyclesBetweenActivations) {
            return false;
        }

        _lastActivationCycle = currentCycles;
        return true;
    }

    public void UpdateDesiredFrequency(long desiredFrequency) {
        _cyclesBetweenActivations = this._instructionsPerSecond / desiredFrequency;
    }
}