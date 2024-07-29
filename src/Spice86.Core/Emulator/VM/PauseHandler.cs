namespace Spice86.Core.Emulator.VM;

using Spice86.Shared.Interfaces;

/// <summary>
/// Provides functionality to handle pausing of the emulator.
/// </summary>
public class PauseHandler : IDisposable, IPauseHandler {
    /// <summary>
    /// Delegate for handling pause request events.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">Event data (empty for this event).</param>
    public delegate void PausingEventHandler(object sender, EventArgs e);

    /// <summary>
    /// Delegate for handling resume request events.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">Event data (empty for this event).</param>
    public delegate void ResumedEventHandler(object sender, EventArgs e);

    private readonly ILoggerService _loggerService;
    private readonly ManualResetEvent _manualResetEvent = new(false);
    private bool _disposed;
    private volatile bool _pausing;

    /// <summary>
    /// Initializes a new instance of the <see cref="PauseHandler"/> class with the specified logger service.
    /// </summary>
    /// <param name="loggerService">The logger service to use for logging.</param>
    public PauseHandler(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <inheritdoc />
    public bool IsPaused => _pausing;

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _manualResetEvent.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? Pausing;

    /// <inheritdoc />
    public event EventHandler<EventArgs>? Resumed;

    /// <inheritdoc />
    public void RequestPause(string? reason = null) {
        _loggerService.Debug("Pause requested by thread '{Thread}': {Reason}",
            Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(), reason);
        Pausing?.Invoke(this, EventArgs.Empty);
        _pausing = true;
        _manualResetEvent.Reset();
    }

    /// <inheritdoc />
    public void Resume() {
        _loggerService.Debug("Pause ended by thread {Thread}", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        _manualResetEvent.Set();
        _pausing = false;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void WaitIfPaused() {
        if (!_pausing) {
            return;
        }
        _loggerService.Debug("Thread {Thread} is taking a pause", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        _manualResetEvent.WaitOne(Timeout.Infinite);
    }
}