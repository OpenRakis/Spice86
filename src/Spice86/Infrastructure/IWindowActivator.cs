namespace Spice86.Infrastructure;

using Spice86.Core.Emulator;
using Spice86.Interfaces;
using Spice86.ViewModels;

/// <summary>
/// Activates the window associated to a ViewModel
/// </summary>
public interface IWindowActivator {
    /// <summary>
    /// Activates the Window corresponding to the <see cref="DebugWindowViewModel"/>
    /// </summary>
    /// <param name="uiDispatcherTimer">The UI dispatcher timer, in order to execute code after a set amount of time has passed repeatedly.</param>
    /// <param name="programExecutor">The class than can start, pause, and stop the emulation process.</param>
    /// <param name="pauseStatus">The UI class that get or sets whether the emulator is paused.</param>
    void ActivateDebugWindow(IUIDispatcherTimer uiDispatcherTimer, IProgramExecutor programExecutor, IPauseStatus pauseStatus);

    /// <summary>
    /// Closes the debug window
    /// </summary>
    void CloseDebugWindow();
}
