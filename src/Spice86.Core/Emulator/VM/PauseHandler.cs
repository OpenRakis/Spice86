namespace Spice86.Core.Emulator.VM;

using Spice86.Shared.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides functionality to handle pausing of the emulator.
/// </summary>
public class PauseHandler : IPauseHandler {
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
    private readonly object _pauseLock = new();
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
        lock (_pauseLock) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            // Resume all waiting threads before teardown
            _manualResetEvent.Set();
            _manualResetEvent.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public event Action? Pausing;

    /// <inheritdoc/>
    public event Action? Paused;

    /// <inheritdoc />
    public event Action? Resumed;

    /// <inheritdoc />
    public void RequestPause(string? reason = null) {
        lock (_pauseLock) {
            if (_disposed) {
                return;
            }
        }
        _loggerService.LogInformation("Pause requested by thread '{Thread}': {Reason}",
            Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(), reason);
        Pausing?.Invoke();
        lock (_pauseLock) {
            if (_disposed) {
                return;
            }
            _pausing = true;
            _manualResetEvent.Reset();
        }
        Paused?.Invoke();
    }

    /// <inheritdoc />
    public void Resume() {
        _loggerService.LogDebug("Pause ended by thread {Thread}", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        lock (_pauseLock) {
            if (_disposed) {
                return;
            }
            _pausing = false;
            _manualResetEvent.Set();
        }
        Resumed?.Invoke();
    }

    /// <inheritdoc />
    public void WaitIfPaused() {
        if (!_pausing) {
            return;
        }
        if (_disposed) {
            return;
        }
        _loggerService.LogDebug("Thread {Thread} is taking a pause", Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString());
        _manualResetEvent.WaitOne(Timeout.Infinite);
    }
}
