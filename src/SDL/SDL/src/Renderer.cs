using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class Renderer : SafeHandle
    {

        public static RenderDrivers Drivers { get; } = new RenderDrivers();

        protected Renderer() : base(IntPtr.Zero, true)
        {
        }

        public Renderer(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public Renderer(
            Window window,
            int index = -1,
            RendererFlags flags = RendererFlags.None
        ) : this()
        {
            Renderer? rend = Create(window, index, flags);
            SetHandle(rend.handle);
            rend.SetHandle(IntPtr.Zero);
        }

        public Renderer(Surface surf) : this()
        {
            Renderer? rend = Create(surf);
            SetHandle(rend.handle);
            rend.SetHandle(IntPtr.Zero);
        }

        public RendererInfo Info
        {
            get
            {
                SDL_RendererInfo info;
                SDL_GetRendererInfo(this, out info);
                return new RendererInfo(info);
            }
        }

        public Texture? Target
        {
            get
            {
                IntPtr p = SDL_GetRenderTarget(this);
                if (p == IntPtr.Zero)
                    return null;
                else
                    return new Texture(p, false);
            }
            set
            {
                if (value == null)
                    SDL_SetRenderTarget(this, IntPtr.Zero);
                else
                    SDL_SetRenderTarget(this, value);
            }
        }


        public BlendMode BlendMode
        {
            get
            {
                BlendMode mode;
                ErrorIfNegative(SDL_GetRenderDrawBlendMode(this, out mode));
                return mode;
            }
            set
            {
                ErrorIfNegative(SDL_SetRenderDrawBlendMode(this, value));
            }
        }

        public Color Color
        {
            get
            {
                Color c;
                ErrorIfNegative(SDL_GetRenderDrawColor(this, out c.r, out c.g, out c.b, out c.a));
                return c;
            }
            set
            {
                ErrorIfNegative(SDL_SetRenderDrawColor(this, value.r, value.g, value.b, value.a));
            }
        }

        public System.Drawing.SizeF Scale
        {
            get
            {
                float w, h;
                SDL_RenderGetScale(this, out w, out h);
                return new System.Drawing.SizeF(w, h);
            }
            set
            {
                ErrorIfNegative(SDL_RenderSetScale(this, value.Width, value.Height));
            }
        }

        public Rect ClipRect
        {
            get
            {
                Rect clip;
                SDL_RenderGetClipRect(this, out clip);
                return clip;
            }
            set
            {
                if (Rect.IsEmpty(value))
                    ErrorIfNegative(SDL_RenderSetClipRect(this, IntPtr.Zero));
                else
                    ErrorIfNegative(SDL_RenderSetClipRect(this, value));
            }
        }

        public Rect Viewport
        {
            get
            {
                Rect clip;
                SDL_RenderGetViewport(this, out clip);
                return clip;
            }
            set
            {
                if (Rect.IsEmpty(value))
                    ErrorIfNegative(SDL_RenderSetViewport(this, IntPtr.Zero));
                else
                    ErrorIfNegative(SDL_RenderSetViewport(this, value));
            }
        }

        public bool IntegerScale
        {
            get
            {
                SDL_Bool forced = SDL_RenderGetIntegerScale(this);
                if (forced == SDL_Bool.False)
                {
                    SDLException? err = GetError();
                    if (err != null)
                        throw err;
                }
                return forced == SDL_Bool.True;
            }
            set
            {
                ErrorIfNegative(SDL_RenderSetIntegerScale(this, value ? SDL_Bool.True : SDL_Bool.False));
            }
        }

        public System.Drawing.Size OutputSize
        {
            get
            {
                int w, h;
                ErrorIfNegative(SDL_GetRendererOutputSize(this, out w, out h));
                return new System.Drawing.Size(w, h);
            }
        }

        public System.Drawing.Size LogicalSize
        {
            get
            {
                int w, h;
                SDL_RenderGetLogicalSize(this, out w, out h);
                return new System.Drawing.Size(w, h);
            }
            set
            {
                ErrorIfNegative(SDL_RenderSetLogicalSize(this, value.Width, value.Height));
            }
        }

        public void Clear()
        {
            ErrorIfNegative(SDL_RenderClear(this));
        }

        public void Present()
        {
            SDL_RenderPresent(this);
        }

        public void Copy(
          Texture texture,
          in Rect dst,
          double angle = 0,
          RendererFlip flip = RendererFlip.None
        )
        {
            if (angle != 0 || flip != RendererFlip.None)
                SDL_RenderCopyEx(this, texture, IntPtr.Zero, dst, angle, IntPtr.Zero, flip);
            else
                SDL_RenderCopy(this, texture, IntPtr.Zero, dst);
        }

        public void Copy(
          Texture texture,
          in Rect src,
          in Rect dst,
          double angle = 0,
          RendererFlip flip = RendererFlip.None
        )
        {
            if (angle != 0 || flip != RendererFlip.None)
                SDL_RenderCopyEx(this, texture, src, dst, angle, IntPtr.Zero, flip);
            else
                SDL_RenderCopy(this, texture, src, dst);
        }

        public void Copy(
          Texture texture,
          in Rect dst,
          in Point center,
          double angle = 0,
          RendererFlip flip = RendererFlip.None
        )
        {
            SDL_RenderCopyEx(this, texture, IntPtr.Zero, dst, angle, center, flip);
        }

        public void Copy(
          Texture texture,
          in Rect src,
          in Rect dst,
          in Point center,
          double angle = 0,
          RendererFlip flip = RendererFlip.None
        )
        {
            SDL_RenderCopyEx(this, texture, src, dst, angle, center, flip);
        }

        public void DrawLine(Point a, Point b)
          => DrawLine(a.x, a.y, b.x, b.y);

        public void DrawLine(int x1, int y1, int x2, int y2)
        {
            ErrorIfNegative(SDL_RenderDrawLine(this, x1, y1, x2, y2));
        }

        public unsafe void DrawLines(ReadOnlySpan<Point> points)
        {
            fixed (Point* ptr = &MemoryMarshal.GetReference(points))
                ErrorIfNegative(SDL_RenderDrawLines(this, ptr, points.Length));
        }

        public void DrawPoint(Point p)
          => DrawPoint(p.x, p.y);

        public void DrawPoint(int x, int y)
        {
            ErrorIfNegative(SDL_RenderDrawPoint(this, x, y));
        }

        public unsafe void DrawPoints(ReadOnlySpan<Point> points)
        {
            fixed (Point* ptr = &MemoryMarshal.GetReference(points))
                ErrorIfNegative(SDL_RenderDrawPoints(this, ptr, points.Length));
        }

        public void DrawRect(Rect rect)
        {
            ErrorIfNegative(SDL_RenderDrawRect(this, rect));
        }

        public void DrawRect(int x, int y, int w, int h)
        {
            Rect r;
            r.x = x;
            r.y = y;
            r.w = w;
            r.h = h;
            ErrorIfNegative(SDL_RenderDrawRect(this, r));
        }

        public unsafe void DrawRects(ReadOnlySpan<Rect> rect)
        {
            fixed (Rect* ptr = &System.Runtime.InteropServices.MemoryMarshal.GetReference(rect))
                ErrorIfNegative(SDL_RenderDrawRects(this, ptr, rect.Length));
        }

        public void FillRect(int x, int y, int w, int h)
        {
            Rect r;
            r.x = x;
            r.y = y;
            r.w = w;
            r.h = h;
            FillRect(r);
        }

        public void FillRect(Rect rect)
        {
            SDL_RenderFillRect(this, rect);
        }

        public unsafe void FillRects(ReadOnlySpan<Rect> rect)
        {
            fixed (Rect* ptr = &System.Runtime.InteropServices.MemoryMarshal.GetReference(rect))
                ErrorIfNegative(SDL_RenderFillRects(this, ptr, rect.Length));
        }


        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            SDL_DestroyRenderer(this.handle);
            return true;
        }

        public static Renderer Create(Window window, int index = -1, RendererFlags flags = RendererFlags.None)
          => ErrorIfInvalid(SDL_CreateRenderer(window, index, flags));

        public static Renderer Create(Surface surf)
          => ErrorIfInvalid(SDL_CreateSoftwareRenderer(surf));

        public Texture CreateTexture(Surface surface)
        {
            return ErrorIfInvalid(SDL_CreateTextureFromSurface(this, surface));
        }
        public Texture CreateTexture(uint format, TextureAccess access, int width, int height)
        {
            return ErrorIfInvalid(SDL_CreateTexture(this, format, access, width, height));
        }
    }
}
