namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Counter activator based on emulated cycles
/// </summary>
public class CyclesCounterActivator : CounterActivator {
    private readonly long _instructionsPerSecond;
    private readonly State _state;
    private long _cyclesBetweenActivations;
    private long _lastActivationCycle;

    /// <summary>
    /// Initializes a new instance of the <see cref="CyclesCounterActivator"/> class.
    /// </summary>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="pauseHandler">The class responsible for pausing/resuming emulation.</param>
    /// <param name="instructionsPerSecond">The number of instructions per second between activations.</param>
    /// <param name="multiplier">The initial value for the frequency multiplier.</param>
    public CyclesCounterActivator(State state, IPauseHandler pauseHandler, long instructionsPerSecond, double multiplier) : base (pauseHandler, multiplier) {
        _state = state;
        _instructionsPerSecond = instructionsPerSecond;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void UpdateNonZeroFrequency(double desiredFrequency) {
        _cyclesBetweenActivations = (long)(_instructionsPerSecond / desiredFrequency);
    }
}