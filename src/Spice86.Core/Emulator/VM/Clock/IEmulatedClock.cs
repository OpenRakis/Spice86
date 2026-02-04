namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Represents a source of time within the emulation, abstracting away the underlying mechanism.
/// </summary>
public interface IEmulatedClock {
    /// <summary>
    /// Gets the elapsed time in milliseconds since the clock started.
    /// </summary>
    double ElapsedTimeMs { get; }

    /// <summary>
    /// Gets the full index with sub-millisecond precision, combining elapsed milliseconds
    /// with the current CPU cycle position within the millisecond tick.
    /// Equivalent to DOSBox Staging's PIC_FullIndex().
    /// </summary>
    /// <remarks>
    /// This provides more precise timing than ElapsedTimeMs for audio and other subsystems
    /// that need sub-millisecond accuracy.
    /// </remarks>
    double FullIndex { get; }

    /// <summary>
    /// Gets or sets the start time for the emulated clock.
    /// This represents the initial date/time from which CurrentDateTime is calculated.
    /// </summary>
    DateTime StartTime { get; set; }

    /// <summary>
    /// Gets the current date and time, calculated as StartTime + TimeSpan.FromMilliseconds(ElapsedTimeMs).
    /// </summary>
    DateTime CurrentDateTime { get; }

    /// <summary>
    /// Called when the emulator is paused.
    /// </summary>
    void OnPause();

    /// <summary>
    /// Called when the emulator is resumed from pause.
    /// </summary>
    void OnResume();
}