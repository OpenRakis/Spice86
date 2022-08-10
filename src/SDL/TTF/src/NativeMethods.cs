using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    static unsafe class TTFNativeMethods
    {
        [DllImport("SDL2_ttf")]
        public static extern /*const*/ SDL_Version* TTF_Linked_Version();

        [DllImport("SDL2_ttf")]
        public static extern int TTF_Init();

        [DllImport("SDL2_ttf")]
        public static extern void TTF_Quit();

        [DllImport("SDL2_ttf")]
        public static extern int TTF_WasInit();

        [DllImport("SDL2_ttf")]
        public static extern TTFFont TTF_OpenFont(/*const char*/ byte* file, int ptsize);

        [DllImport("SDL2_ttf")]
        public static extern TTFFont TTF_OpenFontIndex(/*const char*/ byte* file, int ptsize, long index);

        [DllImport("SDL2_ttf")]
        public static extern TTFFont TTF_OpenFontRW(RWOps src, int freesrc, int ptsize);

        [DllImport("SDL2_ttf")]
        public static extern TTFFont TTF_OpenFontIndexRW(RWOps src, int freesrc, int ptsize, long index);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_CloseFont(IntPtr font);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_ByteSwappedUNICODE(int swapped);

        [DllImport("SDL2_ttf")]
        public static extern FontStyle TTF_GetFontStyle(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_SetFontStyle(TTFFont font, FontStyle style);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_GetFontOutline(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_SetFontOutline(TTFFont font, int outline);

        [DllImport("SDL2_ttf")]
        public static extern FontHinting TTF_GetFontHinting(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_SetFontHinting(TTFFont font, FontHinting hinting);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_GetFontKerning(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern void TTF_SetFontKerning(TTFFont font, int kerning);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_FontHeight(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_FontAscent(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_FontDescent(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_FontLineSkip(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern long TTF_FontFaces(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_FontFaceIsFixedWidth(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern /*char*/ byte* TTF_FontFaceFamilyName(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern /*char*/ byte* TTF_FontFaceStyleName(TTFFont font);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_GlyphIsProvided(TTFFont font, ushort ch);

        [DllImport("SDL2_ttf")]
        public static extern int TTF_GlyphMetrics(
          TTFFont font,
          ushort ch,
          out int minx,
          out int maxx,
          out int miny,
          out int maxy,
          out int advance
        );

        [DllImport("SDL2_ttf")]
        public static extern int TTF_SizeUTF8(
          TTFFont font,
          /*const char*/ byte* text,
          out int w,
          out int h
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUTF8_Solid(
          TTFFont font,
          /*const char*/ byte* text,
          Color fg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUTF8_Shaded(
          TTFFont font,
          /*const char*/ byte* text,
          Color fg,
          Color bg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUTF8_Blended(
          TTFFont font,
          /*const char*/ byte* text,
          Color fg
        );

        [DllImport("SDL2_ttf")]
        public static extern int TTF_SizeUNICODE(
          TTFFont font,
          /*const */ ushort* text,
          out int w,
          out int h
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUNICODE_Solid(
          TTFFont font,
          /*const */ ushort* text,
          Color fg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUNICODE_Shaded(
          TTFFont font,
          /*const */ ushort* text,
          Color fg,
          Color bg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderUNICODE_Blended(
          TTFFont font,
          /*const */ ushort* text,
          Color fg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderGlyph_Solid(
          TTFFont font,
          ushort ch,
          Color fg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderGlyph_Shaded(
          TTFFont font,
          ushort ch,
          Color fg,
          Color bg
        );

        [DllImport("SDL2_ttf")]
        public static extern Surface TTF_RenderGlyph_Blended(
          TTFFont font,
          ushort ch,
          Color fg
        );
    }

    [Flags]
    public enum FontStyle
    {
        Normal = 0,
        Bold = 0x1,
        Italic = 0x2,
        Underline = 0x4,
        Strikethrough = 0x8,
    }

    public enum FontHinting
    {
        Normal = 0,
        Light = 1,
        Mono = 2,
        None = 3,
    }
}
