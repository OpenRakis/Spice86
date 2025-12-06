namespace Spice86.Core.Emulator.VM.Clock;

using System.Diagnostics;

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : IEmulatedClock {
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private double _cachedTime;

    public EmulatedClock() {
        _stopwatch.Start();
    }

    public double CurrentTimeMs {
        get {
            // Stopwatch.GetTimestamp can be slow, so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return _cachedTime;
            }
            _cachedTime = _stopwatch.Elapsed.TotalMilliseconds;
            return _cachedTime;
        }
    }
}