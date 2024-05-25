namespace Spice86.Infrastructure;

using Spice86.ViewModels;
using Spice86.Views;

/// <inheritdoc cref="IDebugWindowActivator" />
internal class DebugWindowActivator : IDebugWindowActivator {
    private DebugWindow? _debugWindow;

    /// <inheritdoc />
    public void ActivateDebugWindow(DebugWindowViewModel viewModel) {
        if (_debugWindow is not null) {
            _debugWindow.Activate();
            return;
        }
        _debugWindow = new DebugWindow {
            DataContext = viewModel
        };
        _debugWindow.Show();
        _debugWindow.Closed += (_, _) => _debugWindow = null;
    }

    public void CloseDebugWindow() {
        _debugWindow?.Close();
    }
}
