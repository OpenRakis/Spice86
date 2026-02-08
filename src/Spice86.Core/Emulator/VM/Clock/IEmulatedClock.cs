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
    /// that need sub-millisecond accuracy. Only safe to call from the emulation thread.
    /// Cross-thread consumers (mixer, audio callbacks) should use <see cref="AtomicFullIndex"/>.
    /// </remarks>
    double FullIndex { get; }

    /// <summary>
    /// Thread-safe snapshot of <see cref="FullIndex"/>.
    /// Updated atomically by the emulation thread on every RegulateCycles call.
    /// Equivalent to DOSBox Staging's PIC_AtomicIndex().
    /// </summary>
    /// <remarks>
    /// Cross-thread consumers such as the mixer thread should always use this property
    /// instead of <see cref="FullIndex"/> to avoid torn reads.
    /// </remarks>
    double AtomicFullIndex { get; }

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
    /// Converts an absolute FullIndex time to the corresponding CPU cycle count.
    /// Used by the scheduler to compute cycle thresholds for event processing.
    /// </summary>
    /// <param name="scheduledTime">The FullIndex time to convert.</param>
    /// <returns>The absolute cycle count at which that time is reached.</returns>
    long ConvertTimeToCycles(double scheduledTime);

    /// <summary>
    /// Called when the emulator is paused.
    /// </summary>
    void OnPause();

    /// <summary>
    /// Called when the emulator is resumed from pause.
    /// </summary>
    void OnResume();
}