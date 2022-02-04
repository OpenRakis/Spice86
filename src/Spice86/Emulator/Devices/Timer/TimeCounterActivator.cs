namespace Spice86.Emulator.Devices.Timer;

/// <summary>
/// Counter activator based on real system time
/// </summary>
public class TimeCounterActivator : ICounterActivator {
    private readonly double _multiplier;
    private long _lastActivationTime = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _hundredNanosBetweenTicks;
    private long _ticks = 0;
    public TimeCounterActivator(double multiplier) {
        this._multiplier = multiplier;
    }

    public bool IsActivated() {
        _ticks++;
        if (_ticks % 100 != 0) {
            // System.Diagnostics.Stopwatch.GetTimestamp is quite slow, let's not call it every time.
            return false;
        }
        long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
        long elapsedTime = currentTime - _lastActivationTime;
        if (elapsedTime <= _hundredNanosBetweenTicks) {
            return false;
        }
        _lastActivationTime = currentTime;
        return true;
    }

    public void UpdateDesiredFrequency(long desiredFrequency) {
        _hundredNanosBetweenTicks = (long)(10000000 / (_multiplier * desiredFrequency));
    }
}