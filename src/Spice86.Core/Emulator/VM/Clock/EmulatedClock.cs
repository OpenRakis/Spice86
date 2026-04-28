namespace Spice86.Core.Emulator.VM.Clock;

using System.Diagnostics;

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : ClockBase {
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _delay;

    public EmulatedClock(int? jitterSeed, DateTimeOffset startTime)
        : base(ClockJitter.Create(jitterSeed), startTime) {
        _stopwatch.Start();
    }

    /// <inheritdoc/>
    public override double ElapsedTimeMs {
        get {
            // Stopwatch.GetTimestamp can be slow, so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return field;
            }
            field = _stopwatch.Elapsed.TotalMilliseconds + _jitter.Advance() + _delay.Microseconds;
            return field;
        }
    }

    /// <inheritdoc/>
    protected override void OnPauseCore() => _stopwatch.Stop();

    /// <inheritdoc/>
    protected override void OnResumeCore() => _stopwatch.Start();

    /// <inheritdoc/>
    protected override void OnDisposeCore() => _stopwatch.Stop();

    /// <inheritdoc/>
    public override void Delay(TimeSpan timeSpan) {
        _delay += timeSpan;
    }
}