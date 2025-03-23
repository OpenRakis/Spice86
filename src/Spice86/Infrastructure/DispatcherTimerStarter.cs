namespace Spice86.Infrastructure;

using Avalonia.Threading;

internal static class DispatcherTimerStarter {
    /// <summary>
    /// Starts a new <see cref="DispatcherTimer"/> with the specified <paramref name="interval"/>, <paramref name="priority"/> and <paramref name="callback"/>.
    /// </summary>
    /// <param name="interval">The time between executions of the callback</param>
    /// <param name="priority">The priority of execution for the UI Dispatcher</param>
    /// <param name="callback">The user code to execute</param>
    public static DispatcherTimer StartNewDispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        var dispatcherTimer = new DispatcherTimer(interval, priority, callback);
        dispatcherTimer.Start();
        return dispatcherTimer;
    }
}