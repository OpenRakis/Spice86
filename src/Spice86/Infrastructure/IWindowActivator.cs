namespace Spice86.Infrastructure;

using Avalonia.Controls;

using Spice86.ViewModels;

/// <summary>
/// Activates the window associated to a ViewModel
/// </summary>
public interface IWindowActivator {
    /// <summary>
    /// Activates the Window corresponding to the ViewModel
    /// </summary>
    /// <param name="parameters">The parameters to pass to the ViewModel constructor</param>
    void ActivateAdditionalWindow<T>(params object[] parameters) where T : ViewModelBase;

    /// <summary>
    /// Closes all additionnal windows
    /// </summary>
    void CloseAllAdditionalWindows();
}
