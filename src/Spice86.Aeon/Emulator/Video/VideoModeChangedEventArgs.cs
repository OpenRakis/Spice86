namespace Spice86.Aeon.Emulator.Video; 

/// <summary>
/// Contains information about a video mode change.
/// </summary>
public sealed class VideoModeChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoModeChangedEventArgs"/> class.
    /// </summary>
    /// <param name="trueModeChange">Value indicating whether this is a real video mode change or a soft change in how the mode should be rendered.</param>
    public VideoModeChangedEventArgs(bool trueModeChange)
    {
        TrueModeChange = trueModeChange;
    }

    /// <summary>
    /// Gets a value indicating whether this is a real video mode change or a soft change in how the mode should be rendered.
    /// </summary>
    public bool TrueModeChange { get; }
}