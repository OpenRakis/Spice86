namespace Spice86.Core.Emulator.VM.Clock;

using System.Diagnostics;

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : ClockBase {
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private double _cachedTime;

    public EmulatedClock(DateTime? startTime = null) {
        StartTime = startTime ?? DateTime.UtcNow;
        _stopwatch.Start();
    }

    /// <inheritdoc/>
    public override double ElapsedTimeMs {
        get {
            // Stopwatch.GetTimestamp can be slow, so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return _cachedTime;
            }

            _cachedTime = _stopwatch.Elapsed.TotalMilliseconds;
            return _cachedTime;
        }
    }

    /// <inheritdoc/>
    protected override void OnPauseCore() => _stopwatch.Stop();

    /// <inheritdoc/>
    protected override void OnResumeCore() => _stopwatch.Start();

    /// <inheritdoc/>
    protected override void OnDisposeCore() => _stopwatch.Stop();
}