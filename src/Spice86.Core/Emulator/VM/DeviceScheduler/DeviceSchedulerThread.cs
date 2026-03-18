namespace Spice86.Core.Emulator.VM.DeviceScheduler;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Interfaces;

using System.Diagnostics;
using System.Threading;

/// <summary>
///     Pumps a <see cref="DeviceScheduler"/> on a dedicated background thread with
///     hybrid sleep/spin waiting for sub-millisecond timing precision.
/// </summary>
public sealed class DeviceSchedulerThread : IDisposable {
    /// <summary>
    ///     Threshold in milliseconds below which the thread spin-waits instead of sleeping.
    /// </summary>
    private const double SpinThresholdMs = 2.0;

    private static readonly long TicksPerMs = Stopwatch.Frequency / 1000;

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
        SpinWait spinner = new();

        while (!_shouldStop) {
            _pauseHandler.WaitIfPaused();

            if (_shouldStop) {
                break;
            }

            _scheduler.ProcessEvents();

            double nextEventTime = _scheduler.NextEventTime;
            double currentTime = _clock.ElapsedTimeMs;
            double waitMs = nextEventTime - currentTime;

            if (waitMs > SpinThresholdMs) {
                Thread.Sleep(1);
            } else if (waitMs > 0) {
                spinner.SpinOnce(-1);
            } else {
                // No events or event already due — brief yield to avoid busy-wait
                Thread.Sleep(1);
            }

            spinner.Reset();
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
