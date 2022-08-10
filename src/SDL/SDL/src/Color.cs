using System;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public partial struct Color
    {
        public Color(byte r, byte g, byte b, byte a = 255)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static unsafe void CalculateGammaRamp(float gamma, Span<ushort> target)
        {
            if (target.Length < 256)
                throw new ArgumentException("Target span must have a length of 256 or more", "target");

            fixed (ushort* ptr = &MemoryMarshal.GetReference(target))
                SDL_CalculateGammaRamp(gamma, ptr);
        }

        public static ushort[] CalculateGammaRamp(float gamma)
        {
            ushort[]? buf = new ushort[256];
            CalculateGammaRamp(gamma, buf);
            return buf;
        }

        public static implicit operator System.Drawing.Color(in Color clr)
        {
            return System.Drawing.Color.FromArgb(clr.a, clr.r, clr.g, clr.b);
        }

        public static implicit operator Color(in System.Drawing.Color clr)
        {
            return new Color(clr.R, clr.G, clr.B, clr.A);
        }

        public override string ToString()
        {
            return ((System.Drawing.Color)this).ToString();
        }
    }
}
