namespace Spice86.Aeon.Emulator.Video.Modes
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

        public override void InitializeMode(IAeonVgaCard video) {
            base.InitializeMode(video);
            video.AttributeController.AttributeModeControl = 0x01;
            video.CrtController.CrtModeControl = 0xE3;
            video.Sequencer.SequencerMemoryMode = SequencerMemoryMode.ExtendedMemory;
            video.Graphics.GraphicsMode = 0x00;
            video.CrtController.Overflow = 0x3E;
        }
    }
}
