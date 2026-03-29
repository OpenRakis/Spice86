namespace Spice86.Core.Emulator.VM.DeviceScheduler;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Interfaces;

using System.Threading;

/// <summary>
///     Pumps a <see cref="DeviceScheduler"/> on a dedicated background thread with
///     hybrid sleep/spin waiting for sub-millisecond timing precision.
/// </summary>
public sealed class DeviceSchedulerThread : IDisposable {

    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly IPauseHandler _pauseHandler;
    private readonly Thread _thread;

    private volatile bool _shouldStop;
    private bool _disposed;

    /// <summary>
    ///     Creates and starts the timing thread.
    /// </summary>
    /// <param name="scheduler">The device scheduler to pump.</param>
    /// <param name="clock">The emulated clock used for time queries.</param>
    /// <param name="pauseHandler">Pause handler for pause/resume integration.</param>
    /// <param name="threadName">Name assigned to the background thread.</param>
    public DeviceSchedulerThread(DeviceScheduler scheduler, IEmulatedClock clock,
        IPauseHandler pauseHandler, string threadName) {
        _scheduler = scheduler;
        _clock = clock;
        _pauseHandler = pauseHandler;

        _thread = new Thread(ThreadLoop) {
            Name = threadName,
            IsBackground = true
        };
        _thread.Start();
    }

    private void ThreadLoop() {
        while (!_shouldStop) {
            _pauseHandler.WaitIfPaused();

            if (_shouldStop) {
                break;
            }

            _scheduler.ProcessEvents();

            double? nextEventTime = _scheduler.NextEventTime;
            if(nextEventTime is not null) {
                double currentTime = _clock.ElapsedTimeMs;
                double waitMs = nextEventTime.Value - currentTime;
                if(waitMs > 0) {
                    Thread.Sleep((int)waitMs);
                }
            } else {
                // No events
                Thread.Sleep(1);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _shouldStop = true;
        if (_thread.IsAlive) {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _disposed = true;
    }
}
