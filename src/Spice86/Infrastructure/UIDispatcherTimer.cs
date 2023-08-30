namespace Spice86.Infrastructure;

using Avalonia.Threading;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <inheritdoc cref="IUIDispatcherTimer" />
internal class UIDispatcherTimer : IUIDispatcherTimer {
    public void StartNew(TimeSpan interval, DispatcherPriority priority, EventHandler callback) {
        new DispatcherTimer(interval, priority, callback).Start();
    }
}
