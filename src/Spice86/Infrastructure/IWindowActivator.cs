namespace Spice86.Infrastructure;

using Avalonia.Controls;

using Spice86.Core.Emulator;
using Spice86.Interfaces;
using Spice86.ViewModels;

/// <summary>
/// Activates the window associated to a ViewModel
/// </summary>
public interface IWindowActivator {
    /// <summary>
    /// Activates the Window corresponding to the <see cref="DebugViewModel"/>
    /// </summary>
    /// <param name="parameters">The parameters to pass to the <see cref="DebugViewModel"/> constructor</param>
    void ActivateDebugWindow(IUIDispatcherTimer uiDispatcherTimer, IProgramExecutor programExecutor, IPauseStatus pauseStatus);

    /// <summary>
    /// Closes the debug window
    /// </summary>
    void CloseDebugWindow();
}
