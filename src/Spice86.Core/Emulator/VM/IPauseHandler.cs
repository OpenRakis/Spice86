namespace Spice86.Core.Emulator.VM;

/// <summary>
/// Interface for handling pausing and resuming the emulator.
/// </summary>
public interface IPauseHandler : IDisposable {
    /// <summary>
    /// Gets a value indicating whether the emulator is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Event triggered when a pause is requested on the emulator.
    /// This allows other parts of the application to stop their work.
    /// </summary>
    event Action? Pausing;

    /// <summary>
    /// Event triggered when a pause has taken effect.
    /// This allows other parts of the application to react to pauses,
    /// such as updating UI elements.
    /// </summary>
    event Action? Paused;

    /// <summary>
    /// Event triggered when a resume is requested on the emulator.
    /// </summary>
    event Action? Resumed;

    /// <summary>
    /// Requests to pause the emulator.
    /// </summary>
    void RequestPause(string? reason = null);

    /// <summary>
    /// Requests to resume the emulator.
    /// </summary>
    void Resume();

    /// <summary>
    /// Waits if the emulator is paused.
    /// </summary>
    void WaitIfPaused();
}