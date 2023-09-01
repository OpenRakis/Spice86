namespace Spice86.Infrastructure;

using Avalonia.Threading;

using System;

/// <inheritdoc cref="IUIDispatcherTimer" />
internal class UIDispatcherTimer : IUIDispatcherTimer {
    public void StartNew(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        new DispatcherTimer(interval, priority, callback).Start();
    }
}
