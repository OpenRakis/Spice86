namespace Spice86.ViewModels.Services;

using Avalonia.Threading;

/// <summary>
/// Centralizes UI callbacks to go read the emulator state. Typically done on pause.
/// </summary>
internal static class DispatcherTimerStarter {
    private static readonly Dictionary<(TimeSpan Interval, DispatcherPriority Priority), DispatcherTimer> _timers = new();
    private static readonly Dictionary<(TimeSpan Interval, DispatcherPriority Priority), EventHandler> _callbacks = new();

    /// <summary>
    /// Registers a callback to be executed at a specified interval and priority.
    /// If a timer with the same interval and priority already exists, the callback is added to the existing timer's event handler.
    /// Otherwise, a new timer is created.
    /// </summary>
    /// <param name="interval">The time between executions of the callback.</param>
    /// <param name="priority">The priority of execution for the UI Dispatcher.</param>
    /// <param name="callback">The user code to execute.</param>
    public static DispatcherTimer StartNewDispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        (TimeSpan interval, DispatcherPriority priority) key = (interval, priority);
        if (_timers.TryGetValue(key, out DispatcherTimer? timer)) {
            _callbacks[key] += callback;
            return timer;
        } else {
            _callbacks[key] = callback;
            var newTimer = new DispatcherTimer(interval, priority, (sender, e) => _callbacks[key]?.Invoke(sender, e));
            _timers[key] = newTimer;
            newTimer.Start();
            return newTimer;
        }
    }
}