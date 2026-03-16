namespace Spice86.Shared.Diagnostics;

using Spice86.Shared.Interfaces;
using System;

public class PerformanceTracker {
    private readonly ITimeProvider _timeProvider;
    private long _lastCycles;
    private DateTime _lastPerformanceUpdate;
    private DateTime _pauseTime;
    private bool _isPaused;

    public double InstructionsPerSecond { get; private set; }

    public PerformanceTracker(ITimeProvider timeProvider) {
        _timeProvider = timeProvider;
        _lastPerformanceUpdate = _timeProvider.Now;
    }

    public void OnPause() {
        if (_isPaused) {
            return;
        }
        _isPaused = true;
        _pauseTime = _timeProvider.Now;
        InstructionsPerSecond = 0;
    }

    public void OnResume() {
        if (!_isPaused) {
            return;
        }
        _isPaused = false;
        // Subtract the time spent in pause from the last update time
        // so that the delta time calculation only counts active execution time.
        _lastPerformanceUpdate += (_timeProvider.Now - _pauseTime);
    }

    public void Update(long currentCycles) {
        if (_isPaused) {
            return;
        }

        DateTime now = _timeProvider.Now;
        double elapsedSeconds = (now - _lastPerformanceUpdate).TotalSeconds;
        if (elapsedSeconds > 0) {
            double cyclesDelta = currentCycles - _lastCycles;
            InstructionsPerSecond = cyclesDelta / elapsedSeconds;
            
            _lastCycles = currentCycles;
            _lastPerformanceUpdate = now;
        }
    }
}
