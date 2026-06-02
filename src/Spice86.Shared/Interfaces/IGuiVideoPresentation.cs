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
    /// On video mode change: update the video source resolution for the GUI.
    /// </summary>
    /// <param name="videoWidth">The width in pixels</param>
    /// <param name="videoHeight">The height in pixels</param>
    void UpdateResolution(int videoWidth, int videoHeight);

    /// <summary>
    /// Invoked when the GUI asks the VideoCard to render the screen contents in the WriteableBitmap's buffer pointer.
    /// </summary>
    event EventHandler<UIRenderEventArgs>? RenderScreen;
}