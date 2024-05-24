namespace Spice86.Infrastructure;

using System;
using Avalonia.Threading;

/// <inheritdoc cref="IUIDispatcherTimer" />
public class UIDispatcherTimer : IUIDispatcherTimer {
    public void StartNew(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        new DispatcherTimer(interval, priority, callback).Start();
    }
}
