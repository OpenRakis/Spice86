namespace Spice86.Core.Emulator.Devices.Timer;

public abstract class CounterActivator {
    /// <summary> True when activation can occur. </summary>
    public abstract bool IsActivated { get; }

    protected abstract void UpdateNonZeroFrequency(double desiredFrequency);

    public double Multiplier {
        get => _multiplier;
        set {
            _multiplier = value;
            // Refresh
            UpdateFrequency();
        }
    }
    private double _multiplier;
    public bool IsFrozen => Multiplier == 0;

    public long Frequency {
        get => _frequency;
        set {
            _frequency = value;
            UpdateFrequency();
        }
    }

    private long _frequency = 1;

    protected CounterActivator(double multiplier) {
        Multiplier = multiplier;
    }

    protected double ComputeActualFrequency(long desiredFrequency) {
        return Multiplier * desiredFrequency;
    }

    private void UpdateFrequency() {
        double computedFrequency = ComputeActualFrequency(Frequency);
        if (computedFrequency == 0) {
            return;
        }
        UpdateNonZeroFrequency(computedFrequency);
    }
}