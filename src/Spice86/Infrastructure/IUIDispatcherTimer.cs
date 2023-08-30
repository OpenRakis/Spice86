namespace Spice86.Infrastructure;

using Avalonia.Threading;

/// <summary>
/// A dispatcher timer runs a callback on the UI thread after a set amount of time has passed, repeatedly.
/// </summary>
public interface IUIDispatcherTimer {
    /// <summary>
    /// Starts a new instance of a DispatcherTimer.
    /// </summary>
    /// <param name="interval">The time between executions of the callback</param>
    /// <param name="priority">The priority of execution for the UI Dispatcher</param>
    /// <param name="callback">The user code to execute</param>
    public void StartNew(TimeSpan interval, DispatcherPriority priority, EventHandler callback);
}
