namespace Spice86.Aeon.Emulator.Video
{
    /// <summary>
    /// Emulates the VGA DAC which provides access to the palette.
    /// </summary>
    public sealed class Dac
    {
        private readonly unsafe uint* palette;
        private readonly UnsafeBuffer<uint> paletteBuffer = new(256);
        private int readChannel;
        private int writeChannel;
        private byte readIndex;
        private byte writeIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="Dac"/> class.
        /// </summary>
        public Dac()
        {
            unsafe
            {
                palette = paletteBuffer.ToPointer();
            }

            Reset();
        }

        /// <summary>
        /// Gets the full 256-color palette.
        /// </summary>
        public ReadOnlySpan<uint> Palette
        {
            get
            {
                unsafe
                {
                    return new ReadOnlySpan<uint>(palette, 256);
                }
            }
        }

        /// <summary>
        /// Gets or sets the current palette read index.
        /// </summary>
        public byte ReadIndex
        {
            get => readIndex;
            set
            {
                readIndex = value;
                readChannel = 0;
            }
        }
        /// <summary>
        /// Gets or sets the current palette write index.
        /// </summary>
        public byte WriteIndex
        {
            get => writeIndex;
            set
            {
                writeIndex = value;
                writeChannel = 0;
            }
        }

        /// <summary>
        /// Reads the next channel in the current color.
        /// </summary>
        /// <returns>Red, green, or blue channel value.</returns>
        public byte Read()
        {
            unsafe
            {
                uint color = palette[readIndex];
                readChannel++;
                if (readChannel == 1)
                {
                    return (byte)((color >> 18) & 0x3F);
                }

                if (readChannel == 2)
                {
                    return (byte)((color >> 10) & 0x3F);
                }

                readChannel = 0;
                readIndex++;
                return (byte)((color >> 2) & 0x3F);
            }
        }
        /// <summary>
        /// Writes the next channel in the current color.
        /// </summary>
        /// <param name="value">Red, green, or blue channel value.</param>
        public void Write(byte value) {
            value &= 0x3F;
            value = (byte)(value << 2 | value >> 4);
            unsafe {
                writeChannel++;
                if (writeChannel == 1) {
                    palette[writeIndex] &= 0xFF00FFFF;
                    palette[writeIndex] |= (uint)(value << 16);
                } else if (writeChannel == 2) {
                    palette[writeIndex] &= 0xFFFF00FF;
                    palette[writeIndex] |= (uint)(value << 8);
                } else {
                    palette[writeIndex] &= 0xFFFFFF00;
                    palette[writeIndex] |= value;
                    writeChannel = 0;
                    writeIndex++;
                }
            }
        }
        /// <summary>
        /// Resets the colors to the default 256-color VGA palette.
        /// </summary>
        public void Reset()
        {
            var source = DefaultPalette;
            for (int i = 0; i < 256; i++)
            {
                uint r = source[i * 3];
                uint g = source[i * 3 + 1];
                uint b = source[i * 3 + 2];
                unsafe
                {
                    palette[i] = b | (g << 8) | (r << 16);
                }
            }
        }
        /// <summary>
        /// Sets a color to the specified RGB values.
        /// </summary>
        /// <param name="index">Index of color to set.</param>
        /// <param name="r">Red component.</param>
        /// <param name="g">Green component.</param>
        /// <param name="b">Blue component.</param>
        public void SetColor(byte index, byte r, byte g, byte b) {
            r &= 0x3F;
            g &= 0x3F;
            b &= 0x3F;
            uint red = (uint)(r << 2 | r >> 4);
            uint green = (uint)(g << 2 | g >> 4);
            uint blue = (uint)(b << 2 | b >> 4);

            unsafe {
                palette[index] = red << 16 | green << 8 | blue;
            }
        }

        #region DefaultPalette
        private static ReadOnlySpan<byte> DefaultPalette => new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0xA8, 0x00, 0xA8, 0x00, 0x00, 0xA8, 0xA8, 0xA8, 0x00, 0x00, 0xA8,
                0x00, 0xA8, 0xA8, 0x54, 0x00, 0xA8, 0xA8, 0xA8, 0x54, 0x54, 0x54, 0x54, 0x54, 0xFC, 0x54, 0xFC,
                0x54, 0x54, 0xFC, 0xFC, 0xFC, 0x54, 0x54, 0xFC, 0x54, 0xFC, 0xFC, 0xFC, 0x54, 0xFC, 0xFC, 0xFC,
                0x00, 0x00, 0x00, 0x14, 0x14, 0x14, 0x20, 0x20, 0x20, 0x2C, 0x2C, 0x2C, 0x38, 0x38, 0x38, 0x44,
                0x44, 0x44, 0x50, 0x50, 0x50, 0x60, 0x60, 0x60, 0x70, 0x70, 0x70, 0x80, 0x80, 0x80, 0x90, 0x90,
                0x90, 0xA0, 0xA0, 0xA0, 0xB4, 0xB4, 0xB4, 0xC8, 0xC8, 0xC8, 0xE0, 0xE0, 0xE0, 0xFC, 0xFC, 0xFC,
                0x00, 0x00, 0xFC, 0x40, 0x00, 0xFC, 0x7C, 0x00, 0xFC, 0xBC, 0x00, 0xFC, 0xFC, 0x00, 0xFC, 0xFC,
                0x00, 0xBC, 0xFC, 0x00, 0x7C, 0xFC, 0x00, 0x40, 0xFC, 0x00, 0x00, 0xFC, 0x40, 0x00, 0xFC, 0x7C,
                0x00, 0xFC, 0xBC, 0x00, 0xFC, 0xFC, 0x00, 0xBC, 0xFC, 0x00, 0x7C, 0xFC, 0x00, 0x40, 0xFC, 0x00,
                0x00, 0xFC, 0x00, 0x00, 0xFC, 0x40, 0x00, 0xFC, 0x7C, 0x00, 0xFC, 0xBC, 0x00, 0xFC, 0xFC, 0x00,
                0xBC, 0xFC, 0x00, 0x7C, 0xFC, 0x00, 0x40, 0xFC, 0x7C, 0x7C, 0xFC, 0x9C, 0x7C, 0xFC, 0xBC, 0x7C,
                0xFC, 0xDC, 0x7C, 0xFC, 0xFC, 0x7C, 0xFC, 0xFC, 0x7C, 0xDC, 0xFC, 0x7C, 0xBC, 0xFC, 0x7C, 0x9C,
                0xFC, 0x7C, 0x7C, 0xFC, 0x9C, 0x7C, 0xFC, 0xBC, 0x7C, 0xFC, 0xDC, 0x7C, 0xFC, 0xFC, 0x7C, 0xDC,
                0xFC, 0x7C, 0xBC, 0xFC, 0x7C, 0x9C, 0xFC, 0x7C, 0x7C, 0xFC, 0x7C, 0x7C, 0xFC, 0x9C, 0x7C, 0xFC,
                0xBC, 0x7C, 0xFC, 0xDC, 0x7C, 0xFC, 0xFC, 0x7C, 0xDC, 0xFC, 0x7C, 0xBC, 0xFC, 0x7C, 0x9C, 0xFC,
                0xB4, 0xB4, 0xFC, 0xC4, 0xB4, 0xFC, 0xD8, 0xB4, 0xFC, 0xE8, 0xB4, 0xFC, 0xFC, 0xB4, 0xFC, 0xFC,
                0xB4, 0xE8, 0xFC, 0xB4, 0xD8, 0xFC, 0xB4, 0xC4, 0xFC, 0xB4, 0xB4, 0xFC, 0xC4, 0xB4, 0xFC, 0xD8,
                0xB4, 0xFC, 0xE8, 0xB4, 0xFC, 0xFC, 0xB4, 0xE8, 0xFC, 0xB4, 0xD8, 0xFC, 0xB4, 0xC4, 0xFC, 0xB4,
                0xB4, 0xFC, 0xB4, 0xB4, 0xFC, 0xC4, 0xB4, 0xFC, 0xD8, 0xB4, 0xFC, 0xE8, 0xB4, 0xFC, 0xFC, 0xB4,
                0xE8, 0xFC, 0xB4, 0xD8, 0xFC, 0xB4, 0xC4, 0xFC, 0x00, 0x00, 0x70, 0x1C, 0x00, 0x70, 0x38, 0x00,
                0x70, 0x54, 0x00, 0x70, 0x70, 0x00, 0x70, 0x70, 0x00, 0x54, 0x70, 0x00, 0x38, 0x70, 0x00, 0x1C,
                0x70, 0x00, 0x00, 0x70, 0x1C, 0x00, 0x70, 0x38, 0x00, 0x70, 0x54, 0x00, 0x70, 0x70, 0x00, 0x54,
                0x70, 0x00, 0x38, 0x70, 0x00, 0x1C, 0x70, 0x00, 0x00, 0x70, 0x00, 0x00, 0x70, 0x1C, 0x00, 0x70,
                0x38, 0x00, 0x70, 0x54, 0x00, 0x70, 0x70, 0x00, 0x54, 0x70, 0x00, 0x38, 0x70, 0x00, 0x1C, 0x70,
                0x38, 0x38, 0x70, 0x44, 0x38, 0x70, 0x54, 0x38, 0x70, 0x60, 0x38, 0x70, 0x70, 0x38, 0x70, 0x70,
                0x38, 0x60, 0x70, 0x38, 0x54, 0x70, 0x38, 0x44, 0x70, 0x38, 0x38, 0x70, 0x44, 0x38, 0x70, 0x54,
                0x38, 0x70, 0x60, 0x38, 0x70, 0x70, 0x38, 0x60, 0x70, 0x38, 0x54, 0x70, 0x38, 0x44, 0x70, 0x38,
                0x38, 0x70, 0x38, 0x38, 0x70, 0x44, 0x38, 0x70, 0x54, 0x38, 0x70, 0x60, 0x38, 0x70, 0x70, 0x38,
                0x60, 0x70, 0x38, 0x54, 0x70, 0x38, 0x44, 0x70, 0x50, 0x50, 0x70, 0x58, 0x50, 0x70, 0x60, 0x50,
                0x70, 0x68, 0x50, 0x70, 0x70, 0x50, 0x70, 0x70, 0x50, 0x68, 0x70, 0x50, 0x60, 0x70, 0x50, 0x58,
                0x70, 0x50, 0x50, 0x70, 0x58, 0x50, 0x70, 0x60, 0x50, 0x70, 0x68, 0x50, 0x70, 0x70, 0x50, 0x68,
                0x70, 0x50, 0x60, 0x70, 0x50, 0x58, 0x70, 0x50, 0x50, 0x70, 0x50, 0x50, 0x70, 0x58, 0x50, 0x70,
                0x60, 0x50, 0x70, 0x68, 0x50, 0x70, 0x70, 0x50, 0x68, 0x70, 0x50, 0x60, 0x70, 0x50, 0x58, 0x70,
                0x00, 0x00, 0x40, 0x10, 0x00, 0x40, 0x20, 0x00, 0x40, 0x30, 0x00, 0x40, 0x40, 0x00, 0x40, 0x40,
                0x00, 0x30, 0x40, 0x00, 0x20, 0x40, 0x00, 0x10, 0x40, 0x00, 0x00, 0x40, 0x10, 0x00, 0x40, 0x20,
                0x00, 0x40, 0x30, 0x00, 0x40, 0x40, 0x00, 0x30, 0x40, 0x00, 0x20, 0x40, 0x00, 0x10, 0x40, 0x00,
                0x00, 0x40, 0x00, 0x00, 0x40, 0x10, 0x00, 0x40, 0x20, 0x00, 0x40, 0x30, 0x00, 0x40, 0x40, 0x00,
                0x30, 0x40, 0x00, 0x20, 0x40, 0x00, 0x10, 0x40, 0x20, 0x20, 0x40, 0x28, 0x20, 0x40, 0x30, 0x20,
                0x40, 0x38, 0x20, 0x40, 0x40, 0x20, 0x40, 0x40, 0x20, 0x38, 0x40, 0x20, 0x30, 0x40, 0x20, 0x28,
                0x40, 0x20, 0x20, 0x40, 0x28, 0x20, 0x40, 0x30, 0x20, 0x40, 0x38, 0x20, 0x40, 0x40, 0x20, 0x38,
                0x40, 0x20, 0x30, 0x40, 0x20, 0x28, 0x40, 0x20, 0x20, 0x40, 0x20, 0x20, 0x40, 0x28, 0x20, 0x40,
                0x30, 0x20, 0x40, 0x38, 0x20, 0x40, 0x40, 0x20, 0x38, 0x40, 0x20, 0x30, 0x40, 0x20, 0x28, 0x40,
                0x2C, 0x2C, 0x40, 0x30, 0x2C, 0x40, 0x34, 0x2C, 0x40, 0x3C, 0x2C, 0x40, 0x40, 0x2C, 0x40, 0x40,
                0x2C, 0x3C, 0x40, 0x2C, 0x34, 0x40, 0x2C, 0x30, 0x40, 0x2C, 0x2C, 0x40, 0x30, 0x2C, 0x40, 0x34,
                0x2C, 0x40, 0x3C, 0x2C, 0x40, 0x40, 0x2C, 0x3C, 0x40, 0x2C, 0x34, 0x40, 0x2C, 0x30, 0x40, 0x2C,
                0x2C, 0x40, 0x2C, 0x2C, 0x40, 0x30, 0x2C, 0x40, 0x34, 0x2C, 0x40, 0x3C, 0x2C, 0x40, 0x40, 0x2C,
                0x3C, 0x40, 0x2C, 0x34, 0x40, 0x2C, 0x30, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
        #endregion
    }
}