using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.TTFNativeMethods;

namespace SDLSharp
{
    public class TTFFont : SafeHandle
    {

        internal TTFFont() : base(IntPtr.Zero, true) { }

        public TTFFont(IntPtr ptr, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(ptr);
        }

        public FontStyle Style
        {
            get => TTF_GetFontStyle(this);
            set => TTF_SetFontStyle(this, value);
        }

        public int Outline
        {
            get => TTF_GetFontOutline(this);
            set => TTF_SetFontOutline(this, value);
        }

        public FontHinting Hinting
        {
            get => TTF_GetFontHinting(this);
            set => TTF_SetFontHinting(this, value);
        }

        public bool Kerning
        {
            get => TTF_GetFontKerning(this) != 0;
            set => TTF_SetFontKerning(this, value ? 1 : 0);
        }

        public int Height => TTF_FontHeight(this);
        public int Ascent => TTF_FontAscent(this);
        public int Descent => TTF_FontDescent(this);
        public int LineSkip => TTF_FontLineSkip(this);
        public bool Monospace => TTF_FontFaceIsFixedWidth(this) != 0;
        public long Faces => TTF_FontFaces(this);
        public unsafe string? FamilyName => UTF8ToString(TTF_FontFaceFamilyName(this));
        public unsafe string? StyleName => UTF8ToString(TTF_FontFaceStyleName(this));

        public bool ProvidesGlyph(char c)
          => TTF_GlyphIsProvided(this, c) != 0;

        public GlyphMetrics GetMetrics(char c)
          => new GlyphMetrics(this, c);

        public unsafe System.Drawing.Size MeasureString(string s)
        {
            int l = SL(s);
            Span<byte> buf = l > 1024 ? new byte[l] : stackalloc byte[l];
            StringToUTF8(s, buf);
            int w, h;
            fixed (byte* p = buf)
                ErrorIfNegative(TTF_SizeUTF8(this, p, out w, out h));
            return new System.Drawing.Size(w, h);
        }

        public unsafe Surface RenderSolid(string s, in Color fg)
        {
            int l = SL(s);
            Span<byte> buf = l > 1024 ? new byte[l] : stackalloc byte[l];
            StringToUTF8(s, buf);
            fixed (byte* p = buf)
                return ErrorIfInvalid(TTF_RenderUTF8_Solid(this, p, fg));
        }

        public unsafe Surface RenderShaded(string s, in Color fg, in Color bg)
        {
            int l = SL(s);
            Span<byte> buf = l > 1024 ? new byte[l] : stackalloc byte[l];
            StringToUTF8(s, buf);
            fixed (byte* p = buf)
                return ErrorIfInvalid(TTF_RenderUTF8_Shaded(this, p, fg, bg));
        }

        public unsafe Surface RenderBlended(string s, in Color fg)
        {
            int l = SL(s);
            Span<byte> buf = l > 1024 ? new byte[l] : stackalloc byte[l];
            StringToUTF8(s, buf);
            fixed (byte* p = buf)
                return ErrorIfInvalid(TTF_RenderUTF8_Blended(this, p, fg));
        }

        public Surface RenderSolid(char glyph, in Color fg)
        {
            return ErrorIfInvalid(TTF_RenderGlyph_Solid(this, glyph, fg));
        }

        public Surface RenderShaded(char glyph, in Color fg, in Color bg)
        {
            return ErrorIfInvalid(TTF_RenderGlyph_Shaded(this, glyph, fg, bg));
        }

        public Surface RenderBlended(char glyph, in Color fg)
        {
            return ErrorIfInvalid(TTF_RenderGlyph_Blended(this, glyph, fg));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            TTF_CloseFont(handle);
            return true;
        }

        public override string ToString()
        {
            string? name = FamilyName;
            string? style = StyleName;

            if (name != null && style != null)
            {
                return $"TTFFont '{name} {style}'";
            }
            else if (name != null)
            {
                return $"TTFFont '{name}'";
            }
            else
            {
                return "TTFFont";
            }
        }
    }
}
