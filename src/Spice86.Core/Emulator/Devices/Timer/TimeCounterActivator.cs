namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Counter activator based on real system time
/// </summary>
public class TimeCounterActivator : CounterActivator {
    private long _lastActivationTime = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _timeBetweenTicks;
    private long _ticks = 0;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeCounterActivator"/> class.
    /// </summary>
    public TimeCounterActivator(IPauseHandler pauseHandler, double multiplier) : base(pauseHandler, multiplier) {
    }

    /// <inheritdoc />
    public override bool IsActivated {
        get {
            _ticks++;
            if (_ticks % 100 != 0) {
                // System.Diagnostics.Stopwatch.GetTimestamp is quite slow, let's not call it every time.
                return false;
            }
            if (IsFrozen) {
                return false;
            }
            long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
            long elapsedTime = currentTime - _lastActivationTime;
            if (elapsedTime <= _timeBetweenTicks) {
                return false;
            }
            _lastActivationTime = currentTime;
            return true;
        }
    }

    /// <inheritdoc />
    protected override void UpdateNonZeroFrequency(double desiredFrequency) {
        _timeBetweenTicks = (long)(System.Diagnostics.Stopwatch.Frequency / desiredFrequency);
    }
}