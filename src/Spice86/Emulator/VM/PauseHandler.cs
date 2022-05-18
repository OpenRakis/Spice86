namespace Spice86.Emulator.VM;

using Serilog;

using Spice86.Emulator.Errors;

using System.Threading;

public class PauseHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<PauseHandler>();

    private volatile bool _paused;

    private volatile bool _pauseEnded;

    private volatile bool _pauseRequested;

    private readonly object _locked = new();

    public void RequestPause() {
        _pauseRequested = true;
        LogStatus($"{nameof(RequestPause)} finished");
    }

    public void RequestPauseAndWait() {
        LogStatus($"{nameof(RequestPauseAndWait)} started");
        _pauseRequested = true;
        while (!_paused) {
            ;
        }

        LogStatus($"{nameof(RequestPauseAndWait)} finished");
    }

    public void RequestResume() {
        LogStatus($"{nameof(RequestResume)} started");
        _pauseRequested = false;
        lock (_locked) {
            Monitor.PulseAll(this);
        }

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
            lock (_locked) {
                Monitor.Wait(this);
            }
        } catch (ThreadInterruptedException exception) {
            Thread.CurrentThread.Interrupt();
            throw new UnrecoverableException($"Fatal error while waiting paused in {nameof(Await)}", exception);
        }
    }

    private void LogStatus(string message) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("{@Message}: {@PauseRequested},{@Paused},{@PauseEnded}", message, _pauseRequested, _paused, _pauseEnded);
        }
    }
}