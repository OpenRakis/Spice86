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
        public byte PalettePixelMask {
            get;
            set;
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

    }
}