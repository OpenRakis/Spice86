namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Represents a source of time within the emulation, abstracting away the underlying mechanism.
/// </summary>
public interface IEmulatedClock {
    /// <summary>
    /// Gets the current time in milliseconds.
    /// </summary>
    double CurrentTimeMs { get; }
}