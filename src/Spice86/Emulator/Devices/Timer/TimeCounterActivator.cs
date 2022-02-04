namespace Spice86.Emulator.Devices.Timer;

using System;

/// <summary>
/// Counter activator based on real system time
/// </summary>
public class TimeCounterActivator : ICounterActivator {
    private readonly double _multiplier;
    private long _lastActivationTime = Environment.TickCount;
    private long _millisBetweenTicks;

    public TimeCounterActivator(double multiplier) {
        this._multiplier = multiplier;
    }

    public bool IsActivated() {
        long currentTime = Environment.TickCount;
        long elapsedTime = currentTime - _lastActivationTime;
        if (elapsedTime <= _millisBetweenTicks) {
            return false;
        }

        _lastActivationTime = currentTime;
        return true;
    }

    public void UpdateDesiredFrequency(long desiredFrequency) {
        _millisBetweenTicks = (long)(1000 / (_multiplier * desiredFrequency));
    }
}