namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Counter activator based on emulated cycles
/// </summary>
public class CyclesCounterActivator : CounterActivator {
    private readonly long _instructionsPerSecond;
    private readonly State _state;
    private long _cyclesBetweenActivations;
    private long _lastActivationCycle;

    public CyclesCounterActivator(State state, long instructionsPerSecond, double multiplier) : base(multiplier) {
        _state = state;
        _instructionsPerSecond = instructionsPerSecond;
    }

    public override bool IsActivated {
        get {
            if (IsFrozen) {
                return false;
            }
            long currentCycles = _state.Cycles;
            long elapsedInstructions = _state.Cycles - _lastActivationCycle;
            if (elapsedInstructions <= _cyclesBetweenActivations) {
                return false;
            }

            _lastActivationCycle = currentCycles;
            return true;
        }
    }

    protected override void UpdateNonZeroFrequency(double desiredFrequency) {
        _cyclesBetweenActivations = (long)(_instructionsPerSecond / desiredFrequency);
    }
}