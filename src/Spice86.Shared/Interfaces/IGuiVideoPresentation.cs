namespace Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Video;

/// <summary>
/// Displays the output of the emulated graphics card <br/>
/// </summary>
public interface IGuiVideoPresentation {
    /// <summary>
    /// Width of the video source for the GUI to display.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the video source for the GUI to display.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// On video mode change: Set Resolution of the video source for the GUI to display
    /// </summary>
    /// <param name="videoWidth">The width in pixels</param>
    /// <param name="videoHeight">The height in pixels</param>
    /// <param name="pixelAspectRatio">The pixel aspect ratio (width/height). Values less than 1.0 indicate pixels taller than wide.</param>
    void SetResolution(int videoWidth, int videoHeight, double pixelAspectRatio = 1.0);

    /// <summary>
    /// Invoked when the GUI asks the VideoCard to render the screen contents in the WriteableBitmap's buffer pointer.
    /// </summary>
    event EventHandler<UIRenderEventArgs>? RenderScreen;

    /// <summary>
    /// Event raised when the user interface has fully initialized and is able to display the content of the video renderer.
    /// </summary>
    public event Action? UserInterfaceInitialized;
}