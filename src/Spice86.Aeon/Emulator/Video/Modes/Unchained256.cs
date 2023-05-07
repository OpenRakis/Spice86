namespace Spice86.Aeon.Emulator.Video.Modes; 

/// <summary>
/// Provides functionality for planar 256-color VGA modes.
/// </summary>
public sealed class Unchained256 : Planar4
{
    public Unchained256(int width, int height, IAeonVgaCard video)
        : base(width, height, 8, 8, VideoModeType.Graphics, video)
    {
    }
}