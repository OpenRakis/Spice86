namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Represents a source of time within the emulation, abstracting away the underlying mechanism.
/// </summary>
public interface IEmulatedClock : IDisposable {
    /// <summary>
    /// Gets the elapsed time in milliseconds since the clock started.
    /// </summary>
    double ElapsedTimeMs { get; }

    /// <summary>
    /// Gets or sets the start time for the emulated clock.
    /// This represents the initial date/time from which CurrentDateTime is calculated.
    /// </summary>
    DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// Gets the current date and time, calculated as StartTime + TimeSpan.FromMilliseconds(ElapsedTimeMs).
    /// </summary>
    DateTimeOffset CurrentDateTime { get; }

    /// <summary>
    /// Gets a value indicating whether the clock is currently paused or has been disposed.
    /// Once <see cref="IDisposable.Dispose"/> is called this returns <see langword="true"/> permanently,
    /// even if <see cref="OnResume"/> is subsequently called.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Advance the clock by the specified amount of time.
    /// </summary>
    /// <remarks>
    /// Used to simulate blocking I/O delays
    /// </remarks>
    void Delay(TimeSpan timeSpan);

    /// <summary>
    /// Called when the emulator is paused.
    /// </summary>
    void OnPause();

    /// <summary>
    /// Called when the emulator is resumed from pause.
    /// </summary>
    void OnResume();
}