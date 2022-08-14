using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern int SDL_BlitScaled(
          Surface src,
          /* const*/ Rect* srcrect,
          Surface dst,
          Rect* dstrect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_BlitSurface(
          Surface src,
          /* const*/ Rect* srcrect,
          Surface dst,
          Rect* dstrect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ConvertPixels(
          int width,
          int height,
          UInt32 src_format,
          /*const*/ void* src,
          int src_pitch,
          UInt32 dst_format,
          void* dst,
          int dst_pitch
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_ConvertSurface(
          Surface src,
          /*const*/ PixelFormat fmt,
          UInt32 flags
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_ConvertSurfaceFormat(
          Surface src,
          UInt32 pixel_format,
          UInt32 flags
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_CreateRGBSurface(
          UInt32 flags,
          int width,
          int height,
          int depth,
          UInt32 Rmask,
          UInt32 Gmask,
          UInt32 Bmask,
          UInt32 Amask
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_CreateRGBSurfaceFrom(
          void* pixels,
          int width,
          int height,
          int depth,
          int pitch,
          UInt32 Rmask,
          UInt32 Gmask,
          UInt32 Bmask,
          UInt32 Amask
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_CreateRGBSurfaceWithFormat(
          UInt32 flags,
          int width,
          int height,
          int depth,
          UInt32 format
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_CreateRGBSurfaceWithFormatFrom(
          void* pixels,
          int width,
          int height,
          int depth,
          int pitch,
          UInt32 format
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_FillRect(
          Surface dst,
          /*const*/ Rect* rect,
          UInt32 color
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_FillRects(
          Surface dst,
          /*const*/ Rect* rects,
          int count,
          UInt32 color
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_FreeSurface(IntPtr surface);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GetClipRect(
          Surface surface,
          out Rect rect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetColorKey(
          Surface surface,
          out UInt32 key
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetSurfaceAlphaMod(
          Surface surface,
          out byte alpha
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetSurfaceBlendMode(
          Surface surface,
          out BlendMode blendMode
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetSurfaceColorMod(
          Surface surface,
          out byte r,
          out byte g,
          out byte b
        );

        [DllImport(LibSDL2Name)]
        public static extern Surface SDL_LoadBMP_RW(
          RWOps src,
          int freesrc
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_LockSurface(Surface surface);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_LowerBlit(
          Surface surface,
          Rect* srcrect,
          Surface dst,
          Rect* dstrect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_LowerBlitScaled(
          Surface surface,
          Rect* srcrect,
          Surface dst,
          Rect* dstrect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SaveBMP_RW(
          Surface surface,
          RWOps dst,
          int freedst
        );

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_SetClipRect(
          Surface surface,
          /*const*/ Rect* rect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetColorKey(
          Surface surface,
          int flag,
          UInt32 key
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetSurfaceAlphaMod(
          Surface surface,
          byte alpha
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetSurfaceBlendMode(
          Surface surface,
          BlendMode blendMode
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetSurfaceColorMod(
          Surface surface,
          byte r,
          byte g,
          byte b
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetSurfacePalette(
          Surface surface,
          Palette palette
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetSurfaceRLE(
          Surface surface,
          int flag
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_UnlockSurface(Surface surface);

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Surface
        {
            public SurfaceFlags flags;
            public SDL_PixelFormat* format;
            public int w, h;
            public int pitch;
            public void* pixels;
            public void* userdata;
            private int locked;
            private void* lock_data;
            public Rect clip_rect;
            private void* SDL_BlitMap;
            public int refcount;
        }
    }

    [Flags]
    public enum SurfaceFlags
    {
        None = 0,
        SWSurface = 0,
        PreAllocated = 1,
        RLEAccelerated = 2,
        DontFree = 4,
    }
}
