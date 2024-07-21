namespace Spice86.Core.Emulator.VM;

using Spice86.Shared.Interfaces;

/// <summary>
/// Provides functionality to handle pausing of the emulator.
/// </summary>
public class PauseHandler : IDisposable {
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
    private readonly ManualResetEvent _manualResetEvent = new(true);
    private bool _disposed;
    private volatile bool _pauseRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="PauseHandler"/> class with the specified logger service.
    /// </summary>
    /// <param name="loggerService">The logger service to use for logging.</param>
    public PauseHandler(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

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
    /// Pauses the emulator immediately.
    /// </summary>
    public void Pause() {
        RequestPause();
        WaitIfPaused();
    }

    /// <summary>
    /// Requests to pause the emulator.
    /// </summary>
    public void RequestPause() {
        _loggerService.Debug("Pause requested");
        _pauseRequested = true;
    }

    /// <summary>
    /// Requests to resume the emulator.
    /// </summary>
    public void Resume() {
        _loggerService.Debug("Resume requested");
        _pauseRequested = false;
        _manualResetEvent.Set();
    }

    /// <summary>
    /// Waits if the emulator is paused.
    /// </summary>
    public void WaitIfPaused() {
        if (!_pauseRequested) {
            return;
        }
        _loggerService.Debug("Waiting due to pause request");
        OnPausing();
        _manualResetEvent.WaitOne(Timeout.Infinite);
        OnResumed();
    }

    /// <summary>
    /// Method to raise the Pausing event.
    /// </summary>
    protected virtual void OnPausing() {
        Pausing?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Method to raise the Resumed event.
    /// </summary>
    protected virtual void OnResumed() {
        Resumed?.Invoke(this, EventArgs.Empty);
    }
}