using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public readonly struct PixelFormatMask
    {
        public int BitsPerPixel { get; }
        public uint R { get; }
        public uint G { get; }
        public uint B { get; }
        public uint A { get; }

        public uint DataFormat => SDL_MasksToPixelFormatEnum(BitsPerPixel, R, G, B, A);

        public PixelFormatMask(PixelDataFormat dataFormat) : this((uint)dataFormat) { }
        public PixelFormatMask(uint dataFormat)
        {
            int bpp;
            uint r, g, b, a;
            ErrorIfFalse(SDL_PixelFormatEnumToMasks(dataFormat, out bpp, out r, out g, out b, out a));
            this.BitsPerPixel = bpp;
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        public PixelFormatMask(int bpp, uint r, uint g, uint b, uint a)
        {
            this.BitsPerPixel = bpp;
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }
    }
}
