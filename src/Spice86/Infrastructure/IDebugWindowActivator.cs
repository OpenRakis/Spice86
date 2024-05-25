namespace Spice86.Infrastructure;

using Spice86.Core.Emulator;
using Spice86.Interfaces;
using Spice86.ViewModels;

/// <summary>
/// Activates or closes the Debug window
/// </summary>
public interface IDebugWindowActivator {
    /// <summary>
    /// Activates the Window corresponding to the <see cref="DebugWindowViewModel"/>
    /// </summary>
    /// <param name="viewModel">The viewModel for the debug view.</param>
    void ActivateDebugWindow(DebugWindowViewModel viewModel);

    /// <summary>
    /// Closes the debug window
    /// </summary>
    void CloseDebugWindow();
}
