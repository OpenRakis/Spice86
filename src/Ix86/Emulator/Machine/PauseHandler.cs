namespace Ix86.Emulator.Machine;

using Ix86.Emulator.Errors;

using Serilog;

using System.Threading;

public class PauseHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<PauseHandler>();
    private volatile bool _pauseRequested;
    private volatile bool _paused;
    private volatile bool _pauseEnded;
    public virtual void RequestPause()
    {
        _pauseRequested = true;
        LogStatus($"{nameof(RequestPause)} finished");
    }

    public virtual void RequestPauseAndWait()
    {
        LogStatus($"{nameof(RequestPauseAndWait)} started");
        _pauseRequested = true;
        while (!_paused) ;
        LogStatus($"{nameof(RequestPauseAndWait)} finished");
    }

    public virtual void RequestResume()
    {
        LogStatus($"{nameof(RequestResume)} started");
        _pauseRequested = false;
        lock (this)
        {
            Monitor.PulseAll(this);
        }

        LogStatus($"{nameof(RequestResume)} finished");
    }

    public virtual void WaitIfPaused()
    {
        while (_pauseRequested)
        {
            LogStatus($"{nameof(WaitIfPaused)} will wait");
            _paused = true;
            Await();
            LogStatus($"{nameof(WaitIfPaused)} awoke");
        }

        _paused = false;
        _pauseEnded = true;
    }

    private void Await()
    {
        try
        {
            lock (this)
            {
                Monitor.Wait(this);
            }
        }
        catch (ThreadInterruptedException exception)
        {
            Thread.CurrentThread.Interrupt();
            throw new UnrecoverableException($"Fatal error while waiting paused in {nameof(Await)}", exception);
        }
    }

    private void LogStatus(string message)
    {
        _logger.Debug("{@Message}: {@PauseRequested},{@Paused},{@PauseEnded}", message, _pauseRequested, _paused, _pauseEnded);
    }
}
