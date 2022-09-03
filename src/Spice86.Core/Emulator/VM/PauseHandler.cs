namespace Spice86.Core.Emulator.VM;

using Serilog;

using Spice86.Core.Emulator.Errors;
using Spice86.Logging;

using System.Diagnostics;
using System.Threading;

public class PauseHandler : IDisposable {
    private static readonly ILogger _logger = Serilogger.Logger.ForContext<PauseHandler>();

    private volatile bool _paused;

    private volatile bool _pauseEnded;

    private volatile bool _pauseRequested;
    private bool disposedValue;
    private readonly ManualResetEvent _manualResetEvent = new(true);

    public void RequestPause() {
        _pauseRequested = true;
        LogStatus($"{nameof(RequestPause)} finished");
    }

    public void RequestPauseAndWait() {
        LogStatus($"{nameof(RequestPauseAndWait)} started");
        _pauseRequested = true;
        _manualResetEvent.WaitOne(Timeout.Infinite);
        LogStatus($"{nameof(RequestPauseAndWait)} finished");
    }

    public void RequestResume() {
        LogStatus($"{nameof(RequestResume)} started");
        _pauseRequested = false;
        _manualResetEvent.Set();
        LogStatus($"{nameof(RequestResume)} finished");
    }

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
            _manualResetEvent.WaitOne(TimeSpan.FromMilliseconds(1));
        } catch (AbandonedMutexException exception) {
            exception.Demystify();
            Thread.CurrentThread.Interrupt();
            throw new UnrecoverableException($"Fatal error while waiting paused in {nameof(Await)}", exception);
        }
    }

    private void LogStatus(string message) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("{@Message}: {@PauseRequested},{@Paused},{@PauseEnded}", message, _pauseRequested, _paused, _pauseEnded);
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                _manualResetEvent.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}