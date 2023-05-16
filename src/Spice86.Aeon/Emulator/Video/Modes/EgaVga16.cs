namespace Spice86.Aeon.Emulator.Video.Modes; 

/// <summary>
/// Provides functionality for 16-color EGA and VGA video modes.
/// </summary>
public sealed class EgaVga16 : Planar4 {
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EgaVga16"/> class with the specified width, height, video card and mode parameters.
    /// </summary>
    /// <param name="width">The width of the video mode in pixels.</param>
    /// <param name="height">The height of the video mode in pixels.</param>
    /// <param name="fontHeight">The font height of the video mode in pixels.</param>
    /// <param name="video">The VGA card on which the video mode is to be used.</param>
    public EgaVga16(int width, int height, int fontHeight, IAeonVgaCard video)
        : base(width, height, 4, fontHeight, VideoModeType.Graphics, video) {
    }
}