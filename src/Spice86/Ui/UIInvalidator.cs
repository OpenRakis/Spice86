namespace Spice86.Emulator.UI;

using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

/// <summary>
/// Tells AvaloniaUI to redraw a UI Control on the UI Thread.
/// </summary>
public class UIInvalidator {
    private readonly IControl control;
    public UIInvalidator(IControl control) {
        this.control = control;
    }

    /// <summary>
    /// Calls the Dispatcher UIThread to invalidate a Visual
    /// </summary>
    /// <returns>The awaitable Task that will do so when awaited</returns>
    public Task Invalidate() {
        return Dispatcher.UIThread.InvokeAsync(() => this.control.InvalidateVisual());
    }
}
