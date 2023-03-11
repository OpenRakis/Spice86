namespace Aeon.Emulator.Video.Modes
{
    /// <summary>
    /// Provides functionality for 16-color EGA and VGA video modes.
    /// </summary>
    public sealed class EgaVga16 : Planar4
    {
        public EgaVga16(int width, int height, int fontHeight, IAeonVgaCard video)
            : base(width, height, 4, fontHeight, VideoModeType.Graphics, video)
        {
        }
    }
}
