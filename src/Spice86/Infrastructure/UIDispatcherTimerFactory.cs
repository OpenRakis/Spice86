namespace Spice86.Infrastructure;

using System;
using Avalonia.Threading;

/// <inheritdoc cref="IUIDispatcherTimerFactory" />
public class UIDispatcherTimerFactory : IUIDispatcherTimerFactory {
    public void StartNew(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        new DispatcherTimer(interval, priority, callback).Start();
    }
}
