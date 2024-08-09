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
}

/// <summary>
/// Provides access to the UI thread.
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
}