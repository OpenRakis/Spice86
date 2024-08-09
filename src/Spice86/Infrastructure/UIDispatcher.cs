namespace Spice86.Infrastructure;

using Avalonia.Threading;

/// <inheritdoc cref="IUIDispatcher" />
public class UIDispatcher : IUIDispatcher {
    private readonly IDispatcher _dispatcher;

    public UIDispatcher(IDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(Action callback, DispatcherPriority priority = default) {
        if (_dispatcher is Dispatcher avaloniaDispatcher) {
            await avaloniaDispatcher.InvokeAsync(callback, priority);
        }
    }

    public void Post(Action callback, DispatcherPriority priority = default) {
        _dispatcher.Post(callback, priority);
    }
    
    public void StartNewDispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        new DispatcherTimer(interval, priority, callback).Start();
    }
}

/// <summary>
/// Provides acces to the UI thread.
/// </summary>
public interface IUIDispatcher {
    /// <summary>
    /// Runs the <paramref name="callback"/> on the UI thread, in an async call.
    /// </summary>
    /// <param name="callback">The code to run.</param>
    /// <param name="priority">The priority attached to the code.</param>
    /// <returns>A <c>Task</c> representing the async operation.</returns>
    Task InvokeAsync(Action callback, DispatcherPriority priority = default);

    /// <summary>
    /// Runs the <paramref name="callback"/> on the UI thread, in a blocking call.
    /// </summary>
    /// <param name="callback">The code to run.</param>
    /// <param name="priority">The priority attached to the code.</param>
    void Post(Action callback, DispatcherPriority priority = default);
    
    /// <summary>
    /// Starts a new <see cref="DispatcherTimer"/> with the specified <paramref name="interval"/>, <paramref name="priority"/> and <paramref name="callback"/>.
    /// </summary>
    /// <param name="interval">The time between executions of the callback</param>
    /// <param name="priority">The priority of execution for the UI Dispatcher</param>
    /// <param name="callback">The user code to execute</param>
    void StartNewDispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback);
}