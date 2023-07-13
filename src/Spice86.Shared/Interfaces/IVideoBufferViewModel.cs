namespace Spice86.Shared.Interfaces;
/// <summary>
/// Graphical User Interface API of a videobuffer exposed to the Emulator. <br/>
/// This is an instance of a VideoBufferViewModel.
/// </summary>
public interface IVideoBufferViewModel : IDisposable {
    /// <summary>
    /// Draws the content of the video buffer onto the UI.
    /// </summary>
    void Draw();
    
    /// <summary>
    /// Whether the mouse cursor is shown.
    /// </summary>
    bool ShowCursor { get; set; }
    
    /// <summary>
    /// The width of the videobuffer, in pixels.
    /// </summary>
    /// <value></value>
    int Width { get; }
    
    /// <summary>
    /// The height of the videobuffer, in pixels.
    /// </summary>
    /// <value></value>
    int Height { get; }
}
