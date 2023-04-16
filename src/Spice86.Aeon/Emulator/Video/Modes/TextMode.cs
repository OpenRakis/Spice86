namespace Spice86.Aeon.Emulator.Video.Modes
{
    /// <summary>
    /// Implements functionality for text video modes.
    /// </summary>
    public sealed class TextMode : VideoMode
    {
        private const uint BaseAddress = 0x18000;

        private readonly UnsafeBuffer<nint> planesBuffer = new(4);
        private readonly unsafe byte** planes;
        private readonly GraphicsControllerRegisters _graphicsControllerRegisters;
        private readonly SequencerRegisters _sequencerRegisters;
        private readonly uint vramSize;

        public TextMode(int width, int height, int fontHeight, IAeonVgaCard video)
            : base(width, height, 4, false, fontHeight, VideoModeType.Text, video)
        {
            unsafe
            {
                byte* videoRam = (byte*)video.VideoRam.ToPointer();
                byte* vram = videoRam;
                planes = (byte**)planesBuffer.ToPointer();

                planes[0] = vram + PlaneSize * 0;
                planes[1] = vram + PlaneSize * 1;
                planes[2] = vram + PlaneSize * 2;
                planes[3] = vram + PlaneSize * 3;
            }

            _graphicsControllerRegisters = video.GraphicsControllerRegisters;
            _sequencerRegisters = video.SequencerRegisters;
            vramSize = video.TotalVramBytes;
        }

        /// <summary>
        /// Gets a value indicating whether the display mode has a cursor.
        /// </summary>
        internal override bool HasCursor => true;

        /// <summary>
        /// Gets a value indicating whether odd-even write addressing is enabled.
        /// </summary>
        private bool IsOddEvenWriteEnabled => _sequencerRegisters.MemoryModeRegister.OddEvenMode;
        /// <summary>
        /// Gets a value indicating whether odd-even read addressing is enabled.
        /// </summary>
        private bool IsOddEvenReadEnabled => (_graphicsControllerRegisters.GraphicsMode & 0x10) != 0;

        public override byte GetVramByte(uint offset)
        {
            if (offset - BaseAddress >= vramSize)
                return 0;

            unsafe
            {
                uint address = offset - BaseAddress;

                if (IsOddEvenReadEnabled)
                {
                    return planes[address & 1][address >> 1];
                }

                var map = _graphicsControllerRegisters.ReadMapSelect & 0x3;
                if (map == 0 || map == 1)
                    return planes[map][address];
                if (map == 3)
                    return Font[address % 4096];
                return 0;
            }
        }

        public override void SetVramByte(uint offset, byte value)
        {
            if (offset - BaseAddress >= vramSize)
                return;

            unsafe
            {
                uint address = offset - BaseAddress;

                if (IsOddEvenWriteEnabled)
                {
                    planes[address & 1][address >> 1] = value;
                }
                else
                {
                    uint mapMask = _sequencerRegisters.MapMaskRegister.Value;
                    if ((mapMask & 0x01) != 0)
                        planes[0][address] = value;
                    if ((mapMask & 0x02) != 0)
                        planes[1][address] = value;

                    if ((mapMask & 0x04) != 0)
                        Font[(address / 32) * FontHeight + (address % 32)] = value;
                }
            }
        }
        internal override ushort GetVramWord(uint offset)
        {
            uint value = GetVramByte(offset);
            return (ushort)(value | (uint)(GetVramByte(offset + 1u) << 8));
        }
        internal override void SetVramWord(uint offset, ushort value)
        {
            SetVramByte(offset, (byte)value);
            SetVramByte(offset + 1u, (byte)(value >> 8));
        }
        internal override uint GetVramDWord(uint offset)
        {
            uint value = GetVramByte(offset);
            value |= (uint)(GetVramByte(offset + 1u) << 8);
            value |= (uint)(GetVramByte(offset + 2u) << 16);
            value |= (uint)(GetVramByte(offset + 3u) << 24);
            return value;
        }
        internal override void SetVramDWord(uint offset, uint value)
        {
            SetVramByte(offset, (byte)value);
            SetVramByte(offset + 1u, (byte)(value >> 8));
            SetVramByte(offset + 2u, (byte)(value >> 16));
            SetVramByte(offset + 3u, (byte)(value >> 24));
        }
        internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
        {
            int value = index | (foreground << 8) | (background << 12);
            SetVramWord((uint)((y * Stride) + (x * 2)) + BaseAddress, (ushort)value);
        }

        // public override void InitializeMode(IAeonVgaCard video)
        // {
        //     base.InitializeMode(video);
        //     _graphicsControllerRegisters.GraphicsMode = 0x10; // OddEven mode
        //     _graphicsControllerRegisters.MiscellaneousGraphics = 0b00000001;
        //     _sequencerRegisters.SequencerMemoryMode = SequencerMemoryMode.ExtendedMemory | SequencerMemoryMode.OddEvenWriteAddressingDisabled;
        //     _sequencerRegisters.MapMaskRegister.Value = 0x03;
        // }
        /// <summary>
        /// Clears all of the characters and attributes on the active display page.
        /// </summary>
        internal void Clear()
        {
            var total = Width * Height;
            unsafe
            {
                for (int i = 0; i < total; i++)
                {
                    planes[0][(DisplayPageSize * ActiveDisplayPage) + i] = 0;
                    planes[1][(DisplayPageSize * ActiveDisplayPage) + i] = 0;
                }
            }
        }
        /// <summary>
        /// Clears a rectangle in the active display page.
        /// </summary>
        /// <param name="offset">Top left corner of the rectangle to clear.</param>
        /// <param name="width">Width of the rectangle to clear.</param>
        /// <param name="height">Height of the rectangle to clear.</param>
        public void Clear(Point offset, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return;

            int pageOffset = DisplayPageSize * ActiveDisplayPage;

            int y2 = Math.Min(offset.Y + height, Height - 1);
            int x2 = Math.Min(offset.X + width, Width - 1);

            unsafe
            {
                for (int y = offset.Y; y < y2; y++)
                {
                    for (int x = offset.X; x < x2; x++)
                    {
                        int byteOffset = y * Width + x2;

                        planes[0][pageOffset + byteOffset] = 0;
                        planes[1][pageOffset + byteOffset] = 0;
                    }
                }
            }
        }
        /// <summary>
        /// Copies a block of text in the console from one location to another
        /// and clears the source rectangle.
        /// </summary>
        /// <param name="sourceOffset">Top left corner of source rectangle to copy.</param>
        /// <param name="destinationOffset">Top left corner of destination rectangle to copy to.</param>
        /// <param name="width">Width of rectangle to copy.</param>
        /// <param name="height">Height of rectangle to copy.</param>
        /// <param name="backgroundCharacter">Character to fill in the source rectangle.</param>
        /// <param name="backgroundAttribute">Attribute to fill in the source rectangle.</param>
        internal void MoveBlock(Point sourceOffset, Point destinationOffset, int width, int height, byte backgroundCharacter, byte backgroundAttribute)
        {
            byte[,] charBuffer = new byte[height, width];
            byte[,] attrBuffer = new byte[height, width];

            int pageOffset = DisplayPageSize * ActiveDisplayPage;

            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int offset = (sourceOffset.Y + y) * Width + sourceOffset.X + x;

                        charBuffer[y, x] = planes[0][pageOffset + offset];
                        attrBuffer[y, x] = planes[1][pageOffset + offset];

                        planes[0][pageOffset + offset] = backgroundCharacter;
                        planes[1][pageOffset + offset] = backgroundAttribute;
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (x + destinationOffset.X >= 0 && y + destinationOffset.Y >= 0)
                        {
                            int offset = (destinationOffset.Y + y) * Width + destinationOffset.X + x;

                            planes[0][pageOffset + offset] = charBuffer[y, x];
                            planes[1][pageOffset + offset] = attrBuffer[y, x];
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Returns the character at the specified coordinates.
        /// </summary>
        /// <param name="x">Horizontal character coordinate.</param>
        /// <param name="y">Vertical character coordinate.</param>
        /// <returns>Character and attribute at this specified position.</returns>
        internal ushort GetCharacter(int x, int y)
        {
            int pageOffset = DisplayPageSize * ActiveDisplayPage;

            unsafe
            {
                int offset = y * Width + x;
                return (ushort)(planes[0][pageOffset + offset] | (planes[1][pageOffset + offset] << 8));
            }
        }
        /// <summary>
        /// Scrolls lines of text up in a rectangle on the active display page.
        /// </summary>
        /// <param name="x1">Left coordinate of scroll region.</param>
        /// <param name="y1">Top coordinate of scroll region.</param>
        /// <param name="x2">Right coordinate of scroll region.</param>
        /// <param name="y2">Bottom coordinate of scroll region.</param>
        /// <param name="lines">Number of lines to scroll.</param>
        /// <param name="backgroundAttribute">Attribute to fill in bottom rows.</param>
        public void ScrollUp(int x1, int y1, int x2, int y2, int lines, byte backgroundAttribute)
        {
            int pageOffset = DisplayPageSize * ActiveDisplayPage;

            unsafe
            {
                for (int l = 0; l < lines; l++)
                {
                    for (int y = y2 - 1; y >= y1; y--)
                    {
                        for (int x = x1; x <= x2; x++)
                        {
                            int destOffset = pageOffset + y * Width + x;
                            int srcOffset = pageOffset + (y + 1) * Width + x;

                            planes[0][destOffset] = planes[0][srcOffset];
                            planes[1][destOffset] = planes[1][srcOffset];
                        }
                    }

                    for (int x = x1; x <= x2; x++)
                    {
                        int destOffset = pageOffset + y2 * Width + x;

                        planes[0][destOffset] = 0;
                        planes[1][destOffset] = backgroundAttribute;
                    }
                }
            }

            //Point srcOffset = new Point(x1, y1);
            //Point destOffset = new Point(x1, y1 - lines);
            //int width = Math.Abs(x2 - x1 + 1);
            //int height = Math.Abs(y2 - y1 + 1);

            //MoveBlock(srcOffset, destOffset, width, height, background);
            //CursorPosition = new Point(x1, y2);
        }
    }
}
