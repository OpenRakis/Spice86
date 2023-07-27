namespace Spice86.Core.Emulator.VM;

using System.Diagnostics;
using System.Threading;

using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides functionality to handle pausing of the emulator.
/// </summary>
public sealed class PauseHandler : IDisposable {
    private readonly ILoggerService _loggerService;

    private readonly IGui? _gui;

    /// <summary>
    /// Initializes a new instance of the <see cref="PauseHandler"/> class with the specified logger service and GUI.
    /// </summary>
    /// <param name="loggerService">The logger service to use for logging.</param>
    /// <param name="gui">The GUI to use for handling pausing.</param>
    public PauseHandler(ILoggerService loggerService, IGui? gui) {
        _loggerService = loggerService;
        _gui = gui;
    }

    private volatile bool _paused;
    private volatile bool _pauseEnded;
    private volatile bool _pauseRequested;
    private bool _disposed;
    private readonly ManualResetEvent _manualResetEvent = new(true);

    /// <summary>
    /// Requests to pause the emulator.
    /// </summary>
    public void RequestPause() {
        _pauseRequested = true;
        LogStatus($"{nameof(RequestPause)} finished");
    }

    /// <summary>
    /// Requests to pause the emulator and waits until the emulator is paused.
    /// </summary>
    public void RequestPauseAndWait() {
        LogStatus($"{nameof(RequestPauseAndWait)} started");
        _pauseRequested = true;
        if(!_disposed) {
            _manualResetEvent.WaitOne(Timeout.Infinite);
        }
        LogStatus($"{nameof(RequestPauseAndWait)} finished");
    }

    /// <summary>
    /// Requests to resume the emulator.
    /// </summary>
    public void RequestResume() {
        LogStatus($"{nameof(RequestResume)} started");
        _pauseRequested = false;
        if(!_disposed) {
            _manualResetEvent.Set();
            if(_gui?.IsPaused == true) {
                _gui.Play();
            }
        }
        LogStatus($"{nameof(RequestResume)} finished");
    }

    /// <summary>
    /// Waits if the emulator is paused.
    /// </summary>
    public void WaitIfPaused() {
        while (_pauseRequested) {
            LogStatus($"{nameof(WaitIfPaused)} will wait");
            _paused = true;
            Await();
            LogStatus($"{nameof(WaitIfPaused)} awoke");
        }

        _paused = false;
        _pauseEnded = true;
    }

    private void Await() {
        try {
            if(!_disposed) {
                _manualResetEvent.WaitOne(Timeout.Infinite);
            }
        } catch (AbandonedMutexException exception) {
            exception.Demystify();
            Thread.CurrentThread.Interrupt();
            throw new UnrecoverableException($"Fatal error while waiting paused in {nameof(Await)}", exception);
        }
    }

    private void LogStatus(string message) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("{Message}: {PauseRequested},{Paused},{PauseEnded}", message, _pauseRequested, _paused, _pauseEnded);
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _manualResetEvent.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}