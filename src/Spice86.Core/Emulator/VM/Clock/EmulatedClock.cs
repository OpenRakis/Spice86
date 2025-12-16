namespace Spice86.Core.Emulator.VM.Clock;

using System.Diagnostics;

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : IEmulatedClock {
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private double _cachedTime;
    private DateTime _pauseStartTime;
    private TimeSpan _totalPausedTime = TimeSpan.Zero;
    private bool _isPaused;

    public EmulatedClock() {
        _stopwatch.Start();
    }

    public double CurrentTimeMs {
        get {
            // Stopwatch.GetTimestamp can be slow, so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return _cachedTime;
            }
            
            double elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
            double pausedMs = _totalPausedTime.TotalMilliseconds;
            
            if (_isPaused) {
                pausedMs += (DateTime.UtcNow - _pauseStartTime).TotalMilliseconds;
            }
            
            _cachedTime = Math.Max(0, elapsedMs - pausedMs);
            return _cachedTime;
        }
    }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(CurrentTimeMs);

    public void OnPause() {
        if (_isPaused) {
            return;
        }
        _pauseStartTime = DateTime.UtcNow;
        _isPaused = true;
    }

    public void OnResume() {
        if (!_isPaused) {
            return;
        }
        _totalPausedTime += DateTime.UtcNow - _pauseStartTime;
        _isPaused = false;
    }
}