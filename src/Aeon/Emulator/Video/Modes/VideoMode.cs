namespace Aeon.Emulator.Video
{
    /// <summary>
    /// Provides information about an emulated video mode.
    /// </summary>
    public abstract class VideoMode
    {
        /// <summary>
        /// The size of a video RAM plane in bytes.
        /// </summary>
        public const int PlaneSize = 65536;
        /// <summary>
        /// The size of a display page in bytes.
        /// </summary>
        public const int DisplayPageSize = 0x1000 / 2;

        private readonly CrtController crtController;
        private readonly AttributeController attributeController;
        private readonly Dac dac;
        private readonly uint vramSize;

        private protected VideoMode(int width, int height, int bpp, bool planar, int fontHeight, VideoModeType modeType,
            IAeonVgaCard video)
        {
            Width = width;
            Height = height;
            OriginalHeight = height;
            BitsPerPixel = bpp;
            IsPlanar = planar;
            FontHeight = fontHeight;
            VideoModeType = modeType;
            dac = video.Dac;
            crtController = video.CrtController;
            attributeController = video.AttributeController;
            VideoRam = GetVideoRamPointer(video);
            vramSize = video.TotalVramBytes;
        }
        private protected VideoMode(int width, int height, VideoMode baseMode)
        {
            Width = width;
            Height = height;
            BitsPerPixel = baseMode.BitsPerPixel;
            IsPlanar = baseMode.IsPlanar;
            FontHeight = baseMode.FontHeight;
            VideoModeType = baseMode.VideoModeType;
            dac = baseMode.dac;
            crtController = baseMode.crtController;
            attributeController = baseMode.attributeController;
            VideoRam = baseMode.VideoRam;
        }

        /// <summary>
        /// Gets the width of the emulated video mode in pixels or characters.
        /// </summary>
        public int Width { get; }
        /// <summary>
        /// Gets the height of the emulated video mode in pixels or characters.
        /// </summary>
        public int Height { get; internal set; }
        /// <summary>
        /// Gets the original height of the emulated video mode in pixels or characters.
        /// </summary>
        /// <remarks>
        /// This remains set to the original height of the video mode before any changes due
        /// to modifying the value of the vertical end register.
        /// </remarks>
        public int OriginalHeight { get; }
        /// <summary>
        /// Gets the bits per pixel of the emulated video mode.
        /// </summary>
        public int BitsPerPixel { get; }
        /// <summary>
        /// Gets the width of the screen in pixels, even in text modes.
        /// </summary>
        public int PixelWidth => VideoModeType == VideoModeType.Graphics ? Width : Width * 8;
        /// <summary>
        /// Gets the height of the screen in pixels, even in text modes.
        /// </summary>
        public int PixelHeight => VideoModeType == VideoModeType.Graphics ? Height : Height * FontHeight;
        /// <summary>
        /// Gets a value which specifies whether the video mode is text-only or graphical.
        /// </summary>
        public VideoModeType VideoModeType { get; }
        /// <summary>
        /// Gets the number of bytes between rows of pixels.
        /// </summary>
        public virtual int Stride => crtController.Offset * 2;
        /// <summary>
        /// Gets the number of bytes from the beginning of video memory where the display data starts.
        /// </summary>
        public virtual int StartOffset => crtController.StartAddress;
        /// <summary>
        /// Gets the number of pixels to shift the output display horizontally.
        /// </summary>
        public int HorizontalPanning => attributeController.HorizontalPixelPanning;
        /// <summary>
        /// Gets the value to add to StartOffest.
        /// </summary>
        public int BytePanning => (crtController.PresetRowScan >> 5) & 0x3;
        /// <summary>
        /// Gets the value of the LineCompare register.
        /// </summary>
        public int LineCompare => crtController.LineCompare | ((crtController.Overflow & (1 << 4)) << 4) | ((crtController.MaximumScanLine & (1 << 6)) << 3);
        /// <summary>
        /// Gets the value of the StartVerticalBlanking register.
        /// </summary>
        public int StartVerticalBlanking => crtController.StartVerticalBlanking | ((crtController.Overflow & (1 << 3)) << 5) | ((crtController.MaximumScanLine & (1 << 5)) << 4);
        /// <summary>
        /// Gets a pointer to the emulated video RAM.
        /// </summary>
        public IntPtr VideoRam { get; }
        /// <summary>
        /// Gets the current EGA/VGA compatibility map.
        /// </summary>
        public ReadOnlySpan<byte> InternalPalette => attributeController.InternalPalette;
        /// <summary>
        /// Gets the current VGA color palette.
        /// </summary>
        public ReadOnlySpan<uint> Palette => dac.Palette;
        /// <summary>
        /// Gets a value indicating whether the display mode is planar.
        /// </summary>
        public bool IsPlanar { get; }
        /// <summary>
        /// Gets the currently active display page index.
        /// </summary>
        public int ActiveDisplayPage { get; set; }
        /// <summary>
        /// Gets the height of the mode's font in pixels.
        /// </summary>
        public int FontHeight { get; }
        /// <summary>
        /// Gets the current font for the video mode.
        /// </summary>
        public byte[] Font { get; } = new byte[4096];
        /// <summary>
        /// Gets the video mode's width in mouse virtual screen units.
        /// </summary>
        public virtual int MouseWidth => PixelWidth;

        /// <summary>
        /// Gets a value indicating whether the display mode has a cursor.
        /// </summary>
        internal virtual bool HasCursor => false;

        /// <summary>
        /// Reads a byte from an address in video memory.
        /// </summary>
        /// <param name="offset">Address of byte to read.</param>
        /// <returns>Byte at specified address.</returns>
        internal abstract byte GetVramByte(uint offset);
        /// <summary>
        /// Writes a byte to an address in video memory.
        /// </summary>
        /// <param name="offset">Address where byte will be written.</param>
        /// <param name="value">Value to write to specified address.</param>
        public abstract void SetVramByte(uint offset, byte value);
        /// <summary>
        /// Reads a 16-bit word from an address in video memory.
        /// </summary>
        /// <param name="offset">Address of word to read.</param>
        /// <returns>Word at specified address.</returns>
        internal abstract ushort GetVramWord(uint offset);
        /// <summary>
        /// Writes a 16-bit word to an address in video memory.
        /// </summary>
        /// <param name="offset">Address where word will be written.</param>
        /// <param name="value">Value to write to specified address.</param>
        internal abstract void SetVramWord(uint offset, ushort value);
        /// <summary>
        /// Reads a 32-bit doubleword from an address in video memory.
        /// </summary>
        /// <param name="offset">Address of doubleword to read.</param>
        /// <returns>Doubleword at specified address.</returns>
        internal abstract uint GetVramDWord(uint offset);
        /// <summary>
        /// Writes a 32-bit doubleword to an address in video memory.
        /// </summary>
        /// <param name="offset">Address where doubleword will be written.</param>
        /// <param name="value">Value to write to specified address.</param>
        internal abstract void SetVramDWord(uint offset, uint value);
        /// <summary>
        /// Writes a character at a position on the screen with the current font.
        /// </summary>
        /// <param name="x">Column of character to write.</param>
        /// <param name="y">Row of character to write.</param>
        /// <param name="index">Index of character to write.</param>
        /// <param name="foreground">Foreground color of character to write.</param>
        /// <param name="background">Background color of character to write.</param>
        internal abstract void WriteCharacter(int x, int y, int index, byte foreground, byte background);

        /// <summary>
        /// Performs any necessary initialization upon entering the video mode.
        /// </summary>
        /// <param name="video">The video device.</param>
        public virtual void InitializeMode(IAeonVgaCard video)
        {
            // video.VirtualMachine.PhysicalMemory.Bios.CharacterPointHeight = (ushort)FontHeight;

            unsafe
            {
                byte* ptr = (byte*)VideoRam.ToPointer();
                for (int i = 0; i < vramSize; i++)
                    ptr[i] = 0;
            }

            int stride;

            if (VideoModeType == VideoModeType.Text)
            {
                video.TextConsole.Width = Width;
                video.TextConsole.Height = Height;
                stride = Width * 2;
            }
            else
            {
                video.TextConsole.Width = Width / 8;
                video.TextConsole.Height = Height / FontHeight;
                if (BitsPerPixel < 8)
                    stride = Width / 8;
                else
                    stride = Width;
            }

            crtController.Overflow = 1 << 4;
            crtController.MaximumScanLine = 1 << 6;
            crtController.LineCompare = 0xFF;
            crtController.Offset = (byte)(stride / 2u);
            crtController.StartAddress = 0;
            video.Graphics.BitMask = 0xFF;
        }

        /// <summary>
        /// Returns a pointer to video RAM for the display mode.
        /// </summary>
        /// <param name="video">Current VideoHandler instance.</param>
        /// <returns>Pointer to the mode's video RAM.</returns>
        internal virtual nint GetVideoRamPointer(IAeonVgaCard video) => video.VideoRam;
    }
}
