namespace Spice86.Core.Emulator.VM.Clock;

using System.Diagnostics;

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : ClockBase {
    private readonly Stopwatch _stopwatch = new();

    public EmulatedClock(int? jitterSeed, DateTimeOffset startTime)
        : base(ClockJitter.Create(jitterSeed), startTime) {
        _stopwatch.Start();
    }

    /// <inheritdoc/>
    public override double ElapsedTimeMs => _stopwatch.Elapsed.TotalMilliseconds + _jitter.Advance();

    /// <inheritdoc/>
    protected override void OnPauseCore() => _stopwatch.Stop();

    /// <inheritdoc/>
    protected override void OnResumeCore() => _stopwatch.Start();

    /// <inheritdoc/>
    protected override void OnDisposeCore() => _stopwatch.Stop();
}