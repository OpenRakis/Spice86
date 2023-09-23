namespace Spice86.Infrastructure;

using Avalonia.Controls;

using Spice86.Core.Emulator;
using Spice86.Interfaces;
using Spice86.ViewModels;
using Spice86.Views;

using System;
using System.Collections.Generic;

/// <inheritdoc cref="IWindowActivator" />
internal class WindowActivator : IWindowActivator {
    private DebugWindow? _debugWindow;

    /// <inheritdoc />
    public void ActivateDebugWindow(IUIDispatcherTimer uiDispatcherTimer, IProgramExecutor programExecutor, IPauseStatus pauseStatus) {
        if (_debugWindow is not null) {
            _debugWindow.Activate();
            return;
        }
        var viewModel = new DebugViewModel(uiDispatcherTimer, pauseStatus) {
            ProgramExecutor = programExecutor
        };
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
