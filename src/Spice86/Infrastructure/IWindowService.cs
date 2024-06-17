namespace Spice86.Infrastructure;

using Spice86.ViewModels;

/// <summary>
/// Service used for showing the debug window.
/// </summary>
public interface IWindowService {
    /// <summary>
    /// Shows the debug window.
    /// </summary>
    /// <param name="viewModel">The <see cref="DebugWindowViewModel"/> used as DataContext in case the window needs to be created.</param>
    Task ShowDebugWindow(DebugWindowViewModel viewModel);
}