using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.TTFNativeMethods;

namespace SDLSharp
{
    public class GlyphMetrics
    {
        int minx, maxx, miny, maxy, advance;

        public GlyphMetrics(TTFFont font, char glyph)
        {
            ErrorIfNegative(TTF_GlyphMetrics(font, glyph, out minx, out maxx, out miny, out maxy, out advance));
        }

        public int MinX => minx;
        public int MaxX => maxx;
        public int MinY => miny;
        public int MaxY => maxy;
        public int Advance => advance;

        public override string ToString()
        {
            return $"{nameof(GlyphMetrics)} [MinX={minx},MaxX={maxx},MinY={miny},MaxY={maxy},Advance={advance}]";
        }
    }
}
