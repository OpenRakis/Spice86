namespace Spice86.Aeon.Emulator.Video.Modes
{
    /// <summary>
    /// Implements functionality for chained 8-bit 256-color VGA modes.
    /// </summary>
    public sealed class Vga256 : VideoMode
    {
        private readonly unsafe byte* videoRam;

        public Vga256(int width, int height, IAeonVgaCard video) : base(width, height, 8, false, 8, VideoModeType.Graphics, video)
        {
            unsafe
            {
                videoRam = (byte*)video.VideoRam.ToPointer();
            }
        }

        public override int MouseWidth => PixelWidth * 2;

        public override void InitializeMode(IAeonVgaCard video) {
            base.InitializeMode(video);
            video.AttributeController.AttributeModeControl = 0x41;
            video.Sequencer.SequencerMemoryMode = SequencerMemoryMode.Chain4 | SequencerMemoryMode.ExtendedMemory | SequencerMemoryMode.OddEvenWriteAddressingDisabled;
            video.CrtController.MaximumScanLine |= 1;
            video.CrtController.Overflow = 0x1F;
            video.CrtController.CrtModeControl = 0xA3;
        }

        public override byte GetVramByte(uint offset)
        {
            unsafe
            {
                return videoRam[offset];
            }
        }

        public override void SetVramByte(uint offset, byte value)
        {
            unsafe
            {
                videoRam[offset] = value;
            }
        }
        internal override ushort GetVramWord(uint offset)
        {
            unsafe
            {
                return *(ushort*)(videoRam + offset);
            }
        }
        internal override void SetVramWord(uint offset, ushort value)
        {
            unsafe
            {
                *(ushort*)(videoRam + offset) = value;
            }
        }
        internal override uint GetVramDWord(uint offset)
        {
            unsafe
            {
                return *(uint*)(videoRam + offset);
            }
        }
        internal override void SetVramDWord(uint offset, uint value)
        {
            unsafe
            {
                *(uint*)(videoRam + offset) = value;
            }
        }
        internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
        {
            unsafe
            {
                int stride = Stride;
                int startPos = (y * stride * 8) + x * 8;
                byte[] font = Font;

                for (int row = 0; row < 8; row++)
                {
                    uint value = font[index * 8 + row];
                    int pos = startPos + (row * stride);

                    for (int column = 0; column < 8; column++)
                        videoRam[pos + column] = (value & (0x80 >> column)) != 0 ? foreground : background;
                }
            }
        }
    }
}
