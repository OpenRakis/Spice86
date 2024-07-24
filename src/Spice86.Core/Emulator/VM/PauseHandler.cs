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

    /// <summary>
    /// Gets a value indicating whether the emulator is currently paused.
    /// </summary>
    public bool IsPaused => _pausing;

    /// <summary>
    /// Releases the resources used by the PauseHandler.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _manualResetEvent.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Event triggered when a pause is requested on the emulator.
    /// This allows other parts of the application to react to pauses,
    /// such as stopping processing or updating UI elements.
    /// </summary>
    public event EventHandler<EventArgs>? Pausing;

    /// <summary>
    /// Event triggered when a resume is requested on the emulator.
    /// </summary>
    public event EventHandler<EventArgs>? Resumed;

    /// <summary>
    /// Requests to pause the emulator.
    /// </summary>
    public void RequestPause(string? reason = null) {
        _loggerService.Information("Pause requested by thread `{Thread}`: {Reason}",
            Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(), reason);
        Pausing?.Invoke(this, EventArgs.Empty);
        _pausing = true;
        _manualResetEvent.Reset();
    }

    /// <summary>
    /// Requests to resume the emulator.
    /// </summary>
    public void Resume() {
        _loggerService.Information("Pause ended by thread {Thread}", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        _manualResetEvent.Set();
        _pausing = false;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Waits if the emulator is paused.
    /// </summary>
    public void WaitIfPaused() {
        if (!_pausing) {
            return;
        }
        _loggerService.Information("Thread {Thread} is taking a pause", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        _manualResetEvent.WaitOne(Timeout.Infinite);
    }
}