namespace Spice86.Shared.Interfaces;

/// <summary>
/// Interface implemented by the window containing the main video bitmap.
/// </summary>
public interface IScreenPresenter {
    /// <summary>
    /// Event raised when the user interface has started and is able to display the content of the video renderer in a dedicated thread.
    /// </summary>
    public event Action? UserInterfaceInitialized;
}