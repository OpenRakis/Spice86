using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {

        [DllImport("SDL2")]
        public static extern PixelFormat SDL_AllocFormat(
          UInt32 pixel_format
        );

        [DllImport("SDL2")]
        public static extern Palette SDL_AllocPalette(
          int ncolors
        );

        [DllImport("SDL2")]
        public static extern void SDL_CalculateGammaRamp(
          float gamma,
          ushort* ramp
        );

        [DllImport("SDL2")]
        public static extern void SDL_FreeFormat(
          IntPtr format
        );

        [DllImport("SDL2")]
        public static extern void SDL_FreePalette(
          IntPtr palette
        );

        [DllImport("SDL2")]
        public static extern /*const char*/ byte* SDL_GetPixelFormatName(
          UInt32 format
        );

        [DllImport("SDL2")]
        public static extern void SDL_GetRGB(
          UInt32 pixel,
          PixelFormat format,
          out byte r,
          out byte g,
          out byte b
        );

        [DllImport("SDL2")]
        public static extern void SDL_GetRGBA(
          UInt32 pixel,
          PixelFormat format,
          out byte r,
          out byte g,
          out byte b,
          out byte a
        );

        [DllImport("SDL2")]
        public static extern UInt32 SDL_MapRGB(
          PixelFormat format,
          byte r,
          byte g,
          byte b
        );

        [DllImport("SDL2")]
        public static extern UInt32 SDL_MapRGBA(
          PixelFormat format,
          byte r,
          byte g,
          byte b,
          byte a
        );

        [DllImport("SDL2")]
        public static extern UInt32 SDL_MasksToPixelFormatEnum(
          int bpp,
          UInt32 Rmask,
          UInt32 Gmask,
          UInt32 Bmask,
          UInt32 Amask
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_PixelFormatEnumToMasks(
          UInt32 format,
          out int bpp,
          out UInt32 Rmask,
          out UInt32 Gmask,
          out UInt32 Bmask,
          out UInt32 Amask
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetPaletteColors(
          Palette palette,
          /*const*/ Color* colors,
          int firstcolor,
          int ncolors
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetPixelFormatPalette(
          PixelFormat format,
          Palette palette
        );

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_PixelFormat
        {
            public UInt32 format;
            public SDL_Palette* palette;
            public byte BitsPerPixel;
            public byte BytesPerPixel;
            public UInt32 Rmask, Gmask, Bmask, Amask;
            private byte Rloss, Gloss, Bloss, Aloss;
            private byte Rshift, Gshift, Bshift, Ashift;
            private int refcout;
            private SDL_PixelFormat* next;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Palette
        {
            public int ncolors;
            public Color* colors;
            private UInt32 version;
            private int refcount;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct Color
    {
        public byte r, g, b, a;
    }
}
