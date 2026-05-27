namespace Spice86.Tests.UI.CfgCpu;

using Avalonia.Threading;

using Spice86.ViewModels.Services;

/// <summary>
/// Synchronous <see cref="IUIDispatcher"/> stand-in: runs callbacks inline so command
/// continuations execute within the test thread without requiring an Avalonia
/// <see cref="Dispatcher"/> pump.
/// </summary>
internal sealed class InlineUIDispatcher : IUIDispatcher {
    public Task InvokeAsync(Action callback, DispatcherPriority priority) {
        callback();
        return Task.CompletedTask;
    }

    public void Post(Action callback, DispatcherPriority priority) {
        callback();
    }

    public bool CheckAccess() => true;
}
