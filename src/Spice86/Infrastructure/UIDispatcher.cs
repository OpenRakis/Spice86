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
