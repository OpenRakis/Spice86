namespace Spice86.Aeon.Emulator.Video.Modes; 

/// <summary>
/// Provides functionality for planar 256-color VGA modes.
/// </summary>
public sealed class Unchained256 : Planar4 {
    /// <summary>
    /// Initializes a new instance of the <see cref="Unchained256"/> class with the specified width, height, video card and mode parameters.
    /// </summary>
    /// <param name="width">The width of the video mode in pixels.</param>
    /// <param name="height">The height of the video mode in pixels.</param>
    /// <param name="video">The VGA card on which the video mode is to be used.</param>
    public Unchained256(int width, int height, IAeonVgaCard video)
        : base(width, height, 8, 8, VideoModeType.Graphics, video) {
    }
}
