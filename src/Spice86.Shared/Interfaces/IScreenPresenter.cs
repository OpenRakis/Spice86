namespace Spice86.Shared.Interfaces;

/// <summary>
/// Interface implemented by the UI class that presents video updates on screen.
/// </summary>
public interface IScreenPresenter {
    /// <summary>
    /// Event raised when the user interface has started and is able to display the content of the video renderer.
    /// </summary>
    public event Action? UserInterfaceInitialized;

    /// <summary>
    /// Sets the pixel aspect ratio for proper CRT display.
    /// </summary>
    /// <param name="pixelAspectRatio">The pixel aspect ratio (width/height) for the current video mode</param>
    void SetPixelAspectRatio(double pixelAspectRatio);
}