namespace Spice86.Emulator.Devices.Timer;

using System;

/// <summary>
/// Counter activator based on real system time
/// </summary>
public class TimeCounterActivator : ICounterActivator {
    private readonly double _multiplier;
    private long _lastActivationTime = DateTime.Now.Ticks;
    private long _timeBetweenTicks;

    public TimeCounterActivator(double multiplier) {
        this._multiplier = multiplier;
    }

    public bool IsActivated() {
        long currentTime = DateTime.Now.Ticks;
        long elapsedTime = currentTime - _lastActivationTime;
        if (elapsedTime <= _timeBetweenTicks) {
            return false;
        }

        _lastActivationTime = currentTime;
        return true;
    }

    public void UpdateDesiredFrequency(long desiredFrequency) {
        _timeBetweenTicks = (long)(1000000000 / (_multiplier * desiredFrequency));
    }
}