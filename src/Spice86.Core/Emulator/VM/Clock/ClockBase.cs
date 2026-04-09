namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Base class for emulated clock implementations, handling pause, resume, and dispose state.
/// </summary>
public abstract class ClockBase : IEmulatedClock {
    private volatile bool _isPaused;
    private volatile bool _isDisposed;

    /// <summary>The jitter source for this clock instance.</summary>
    private protected readonly ClockJitter _jitter;

    /// <summary>
    /// Initialises the clock with the given jitter source and sets <see cref="StartTime"/> to
    /// <see cref="DateTime.UtcNow"/>.
    /// </summary>
    private protected ClockBase(ClockJitter jitter) {
        _jitter = jitter;
        StartTime = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    public abstract double ElapsedTimeMs { get; }

    /// <inheritdoc/>
    public DateTime StartTime { get; set; }

    /// <inheritdoc/>
    public DateTime CurrentDateTime => StartTime.AddMilliseconds(ElapsedTimeMs);

    /// <inheritdoc/>
    public bool IsPaused => _isPaused || _isDisposed;

    /// <inheritdoc/>
    public void OnPause() {
        _isPaused = true;
        OnPauseCore();
    }

    /// <inheritdoc/>
    public void OnResume() {
        if (!_isDisposed) {
            _isPaused = false;
            OnResumeCore();
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        _isDisposed = true;
        OnDisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called by <see cref="OnPause"/> after the paused state is set. Override to perform additional pause logic.
    /// </summary>
    protected virtual void OnPauseCore() { }

    /// <summary>
    /// Called by <see cref="OnResume"/> after the paused state is cleared. Override to perform additional resume logic.
    /// </summary>
    protected virtual void OnResumeCore() { }

    /// <summary>
    /// Called by <see cref="Dispose"/> before finalization is suppressed. Override to release additional resources.
    /// </summary>
    protected virtual void OnDisposeCore() { }
}
