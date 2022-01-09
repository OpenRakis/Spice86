using Spice86.Emulator.Cpu;

namespace Spice86.Emulator.Devices.Timer;

/// <summary>
/// Counter activator based on emulated cycles
/// </summary>
public class CyclesCounterActivator : ICounterActivator
{
    private readonly State _state;
    private long _lastActivationCycle;
    private long _cyclesBetweenActivations;
    private readonly long _instructionsPerSecond;
    public CyclesCounterActivator(State state, long instructionsPerSecond)
    {
        this._state = state;
        this._instructionsPerSecond = instructionsPerSecond;
    }

    public bool IsActivated()
    {
        long currentCycles = _state.GetCycles();
        long elapsedInstructions = _state.GetCycles() - _lastActivationCycle;
        if (elapsedInstructions <= _cyclesBetweenActivations)
        {
            return false;
        }

        _lastActivationCycle = currentCycles;
        return true;
    }

    public void UpdateDesiredFrequency(long desiredFrequency)
    {
        _cyclesBetweenActivations = this._instructionsPerSecond / desiredFrequency;
    }
}
