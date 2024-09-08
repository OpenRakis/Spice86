namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Base class for counter activators
/// </summary>
public abstract class CounterActivator {
    private readonly IPauseHandler _pauseHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterActivator"/> class.
    /// </summary>
    /// <param name="pauseHandler">The class responsible for pausing/resuming emulation.</param>
    /// <param name="multiplier">The frequency multiplier.</param>
    protected CounterActivator(IPauseHandler pauseHandler, double multiplier) {
        _pauseHandler = pauseHandler;
        Multiplier = multiplier;
    }
    
    /// <summary> True when activation can occur. </summary>
    public abstract bool IsActivated { get; }

    /// <summary>
    /// Updates the frequency of activation based on the desired frequency
    /// </summary>
    /// <param name="desiredFrequency">The desired frequency of activation</param>
    protected abstract void UpdateNonZeroFrequency(double desiredFrequency);

    /// <summary>
    /// Gets or sets the counter activation frequency multiplier
    /// </summary>
    public double Multiplier {
        get => _multiplier;
        set {
            _multiplier = value;
            // Refresh
            UpdateFrequency();
        }
    }
    private double _multiplier;
    
    /// <summary>
    /// Gets a value indicating whether the counter can't be activated.
    /// </summary>
    public bool IsFrozen => Multiplier == 0 || _pauseHandler.IsPaused;

    /// <summary>
    /// Gets or sets the frequency at which the counter is activated.
    /// </summary>
    public long Frequency {
        get => _frequency;
        set {
            _frequency = value;
            UpdateFrequency();
        }
    }

    private long _frequency = 1;

    /// <summary>
    /// Computes the actual frequency based on the desired frequency
    /// </summary>
    /// <param name="desiredFrequency">The desired frequency rate </param>
    /// <returns>The actual frequency</returns>
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