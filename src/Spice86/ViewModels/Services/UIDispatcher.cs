﻿namespace Spice86.ViewModels.Services;

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

    public bool CheckAccess() {
        return _dispatcher.CheckAccess();
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

    /// <summary>
    /// Determines whether the calling thread is the UI thread.
    /// </summary>
    /// <returns><c>true</c> if the calling thread is the UI thread; otherwise, <c>false</c>.</returns>
    bool CheckAccess();
}