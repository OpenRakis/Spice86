using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern BlendMode SDL_ComposeCustomBlendMode(
          int srcColorFactor,
          int dstColorFactor,
          int colorOperation,
          int srcAlphaFactor,
          int dstAlphaFactor,
          int alphaOperation
        );

        [DllImport("SDL2")]
        public static extern Renderer SDL_CreateRenderer(
          Window window,
          int index,
          RendererFlags flags
        );

        [DllImport("SDL2")]
        public static extern Renderer SDL_CreateSoftwareRenderer(
          Surface surface
        );

        [DllImport("SDL2")]
        public static extern Texture SDL_CreateTexture(
          Renderer renderer,
          UInt32 format,
          TextureAccess access,
          int w,
          int h
        );

        [DllImport("SDL2")]
        public static extern Texture SDL_CreateTextureFromSurface(
          Renderer renderer,
          Surface surface
        );

        [DllImport("SDL2")]
        public static extern void SDL_DestroyRenderer(
          IntPtr renderer
        );

        [DllImport("SDL2")]
        public static extern void SDL_DestroyTexture(
          IntPtr texture
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetNumRenderDrivers();

        [DllImport("SDL2")]
        public static extern int SDL_GetRenderDrawBlendMode(
          Renderer renderer,
          out BlendMode blendMode
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetRenderDrawColor(
          Renderer renderer,
          out byte r,
          out byte g,
          out byte b,
          out byte a
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetRenderDriverInfo(
          int index,
          out SDL_RendererInfo info
        );

        [DllImport("SDL2")]
        public static extern IntPtr SDL_GetRenderTarget(Renderer renderer);

        [DllImport("SDL2")]
        public static extern int SDL_GetRendererInfo(
          Renderer renderer,
        out SDL_RendererInfo info
      );

        [DllImport("SDL2")]
        public static extern int SDL_GetRendererOutputSize(
          Renderer renderer,
          out int w,
          out int h
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetTextureAlphaMod(
          Texture texture,
          out byte alpha
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetTextureBlendMode(
          Texture texture,
          out BlendMode blendMode
        );

        [DllImport("SDL2")]
        public static extern int SDL_GetTextureColorMod(
          Texture texture,
          out byte r,
          out byte g,
          out byte b
        );

        [DllImport("SDL2")]
        public static extern int SDL_LockTexture(
          Texture texture,
          in Rect rect,
          out void* pixels,
          out int pitch
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          out UInt32 format,
          out TextureAccess access,
          out int w,
          out int h
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          out UInt32 format,
          IntPtr access,
          IntPtr w,
          IntPtr h
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          IntPtr format,
          IntPtr access,
          out int w,
          out int h
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          IntPtr format,
          IntPtr access,
          IntPtr w,
          out int h
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          IntPtr format,
          IntPtr access,
          out int w,
          IntPtr h
        );

        [DllImport("SDL2")]
        public static extern int SDL_QueryTexture(
          Texture texture,
          IntPtr format,
          out TextureAccess access,
          IntPtr w,
          IntPtr h
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderClear(
          Renderer renderer
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopy(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          in Rect dstrect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopy(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          IntPtr dstrect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopy(
          Renderer renderer,
          Texture texture,
          IntPtr srcrect,
          in Rect dstrect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          in Rect dstrect,
          double angle,
          in Point center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          IntPtr srcrect,
          in Rect dstrect,
          double angle,
          in Point center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          IntPtr dstrect,
          double angle,
          in Point center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          in Rect dstrect,
          double angle,
          IntPtr center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          IntPtr srcrect,
          in Rect dstrect,
          double angle,
          IntPtr center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          in Rect srcrect,
          IntPtr dstrect,
          double angle,
          IntPtr center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderCopyEx(
          Renderer renderer,
          Texture texture,
          IntPtr srcrect,
          IntPtr dstrect,
          double angle,
          IntPtr center,
          RendererFlip flip
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawLine(
          Renderer renderer,
          int x1,
          int y1,
          int x2,
          int y2
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawLines(
          Renderer renderer,
          Point* points,
          int count
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawPoint(
          Renderer renderer,
          int x,
          int y
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawPoints(
          Renderer renderer,
          Point* points,
          int count
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawRect(
          Renderer renderer,
          in Rect rect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderDrawRects(
          Renderer renderer,
          Rect* rects,
          int count
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderFillRect(
          Renderer renderer,
          in Rect rect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderFillRects(
          Renderer renderer,
          Rect* rects,
          int count
        );

        [DllImport("SDL2")]
        public static extern void SDL_RenderGetClipRect(
          Renderer renderer,
          out Rect rect
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RenderGetIntegerScale(
          Renderer renderer
        );

        [DllImport("SDL2")]
        public static extern void SDL_RenderGetLogicalSize(
          Renderer renderer,
          out int w,
          out int h
        );

        [DllImport("SDL2")]
        public static extern void SDL_RenderGetScale(
          Renderer renderer,
          out float scaleX,
          out float scaleY
        );

        [DllImport("SDL2")]
        public static extern void SDL_RenderGetViewport(
          Renderer renderer,
          out Rect rect
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RenderIsClipEnabled(
          Renderer renderer
        );

        [DllImport("SDL2")]
        public static extern void SDL_RenderPresent(
          Renderer renderer
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderReadPixels(
          Renderer renderer,
          in Rect rect,
          UInt32 format,
          void* pixels,
          int pitch
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetClipRect(
          Renderer renderer,
          in Rect rect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetClipRect(
          Renderer renderer,
          IntPtr zero
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetIntegerScale(
          Renderer renderer,
          SDL_Bool enable
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetLogicalSize(
          Renderer renderer,
          int w,
          int h
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetScale(
          Renderer renderer,
          float scaleX,
          float scaleY
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetViewport(
          Renderer renderer,
          in Rect rect
        );

        [DllImport("SDL2")]
        public static extern int SDL_RenderSetViewport(
          Renderer renderer,
          IntPtr rect
        );


        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RenderTargetSupported(
          Renderer renderer
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetRenderDrawBlendMode(
          Renderer renderer,
          BlendMode blendMode
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetRenderDrawColor(
          Renderer renderer,
          byte r,
          byte g,
          byte b,
          byte a
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetRenderTarget(
          Renderer renderer,
          Texture texture
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetRenderTarget(
          Renderer renderer,
          IntPtr texture
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetTextureAlphaMod(
          Texture texture,
          byte alpha
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetTextureBlendMode(
          Texture texture,
          BlendMode blendMode
        );

        [DllImport("SDL2")]
        public static extern int SDL_SetTextureColorMod(
          Texture texture,
          byte r,
          byte g,
          byte b
        );

        [DllImport("SDL2")]
        public static extern void SDL_UnlockTexture(
          Texture texture
        );

        [DllImport("SDL2")]
        public static extern void SDL_UpdateTexture(
          Texture texture,
          in Rect rect,
          in byte pixels,
          int pitch
        );

        [DllImport("SDL2")]
        public static extern void SDL_UpdateYUVTexture(
          Texture texture,
          in Rect rect,
          in byte Yplane,
          int Ypitch,
          in byte Uplane,
          int Upitch,
          in byte Vplane,
          int Vpitch
        );

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_RendererInfo
        {
            public /*const char*/ byte* name;
            public RendererFlags flags;
            public UInt32 num_texture_formats;
            public fixed uint texture_formats[16];
            public int max_texture_width, max_texture_height;
        }
    }

    [Flags]
    public enum RendererFlags : uint
    {
        None = 0,
        Software = 1,
        Accelerated = 2,
        PresentVSync = 4,
        TargetTexture = 8,
    }

    public enum RendererFlip
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    public enum TextureAccess
    {
        Static,
        Streaming,
        Target,
    }

    public enum ScaleMode
    {
        Nearest,
        Linear,
        Best,
    }

    public enum BlendMode
    {
        None = 0,
        Blend = 1,
        Add = 2,
        Mod = 4,
    }

}
