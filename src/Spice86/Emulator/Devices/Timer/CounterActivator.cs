namespace Spice86.Emulator.Devices.Timer;

public abstract class CounterActivator {
    protected CounterActivator(double multiplier) {
        Multiplier = multiplier;
    }

    /// <summary> True when activation can occur. </summary>
    /// <returns> </returns>
    public abstract bool IsActivated { get; }

    public abstract void UpdateDesiredFrequency(long desiredFrequency);

    public double Multiplier { get; set; }

    public bool IsFrozen => Multiplier == 0;

    protected double ComputeActualFrequency(long desiredFrequency) {
        return Multiplier * desiredFrequency;
    }
}