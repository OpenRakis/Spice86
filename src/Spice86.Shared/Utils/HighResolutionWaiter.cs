namespace Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
///     Provides high-resolution waiting helpers that avoid coarse-grained sleeps while preventing busy looping.
/// </summary>
public static class HighResolutionWaiter {
    /// <summary>
    ///     Shared wait handle used to block without resorting to <see cref="Thread.Sleep(int)" />.
    /// </summary>
    private static readonly ManualResetEventSlim WaitHandle = new(false);

    /// <summary>
    ///     Blocks until <paramref name="stopwatch" /> reports an elapsed tick count at or beyond
    ///     <paramref name="targetTicks" />, spinning for very short waits and yielding for longer ones.
    /// </summary>
    /// <param name="stopwatch">Stopwatch providing the current tick count.</param>
    /// <param name="targetTicks">
    ///     Absolute tick value to reach, expressed in stopwatch ticks (see <see cref="Stopwatch.Frequency" />).
    /// </param>
    /// <returns>
    ///     True if the call actually waited, or false if the stopwatch had already advanced beyond
    ///     <paramref name="targetTicks" />.
    /// </returns>
    public static bool WaitUntil(Stopwatch stopwatch, long targetTicks) {
        var spinner = new SpinWait();
        bool waited = false;

        while (true) {
            long remainingTicks = targetTicks - stopwatch.ElapsedTicks;
            if (remainingTicks <= 0) {
                return waited;
            }

            waited = true;

            double remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;

            switch (remainingMilliseconds) {
                case >= 1.0:
                    Wait(TimeSpan.FromTicks(remainingTicks));
                    spinner.Reset();
                    continue;
                case >= 0.05: {
                        spinner.SpinOnce();
                        if (spinner.NextSpinWillYield) {
                            Thread.Yield();
                        }

                        continue;
                    }
                default:
                    spinner.SpinOnce();
                    break;
            }
        }
    }

    /// <summary>
    ///     Blocks for the requested duration by waiting on a reusable <see cref="ManualResetEventSlim" /> rather than
    ///     calling <see cref="Thread.Sleep(int)" />.
    /// </summary>
    /// <param name="duration">Duration to wait.</param>
    /// <remarks>Returns immediately when the requested duration is non-positive.</remarks>
    public static void Wait(TimeSpan duration) {
        if (duration <= TimeSpan.Zero) {
            return;
        }

        WaitHandle.Wait(duration);
    }

    /// <summary>
    ///     Blocks for the requested millisecond duration with high-resolution semantics by delegating to
    ///     <see cref="Wait(TimeSpan)" />.
    /// </summary>
    /// <param name="milliseconds">Duration in milliseconds.</param>
    public static void WaitMilliseconds(double milliseconds) {
        if (milliseconds <= 0) {
            return;
        }

        Wait(TimeSpan.FromMilliseconds(milliseconds));
    }
}