namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Represents a source of time within the emulation, abstracting away the underlying mechanism.
/// </summary>
public interface IEmulatedClock {
    /// <summary>
    /// Gets the current time in milliseconds.
    /// </summary>
    double CurrentTimeMs { get; }

    /// <summary>
    /// Gets or sets the start time for the emulated clock.
    /// This represents the initial date/time from which CurrentDateTime is calculated.
    /// </summary>
    DateTime StartTime { get; set; }

    /// <summary>
    /// Gets the current date and time, calculated as StartTime + TimeSpan.FromMilliseconds(CurrentTimeMs).
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