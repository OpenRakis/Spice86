using System;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public unsafe class Surface : SafeHandle
    {
        internal SDL_Surface* ptr
        {
            get
            {
                if (IsInvalid)
                    throw new ObjectDisposedException(nameof(Surface));
                return (SDL_Surface*)handle;
            }
        }

        protected Surface() : base(IntPtr.Zero, true)
        {
        }

        public Surface(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public int Width => ptr->w;
        public int Height => ptr->h;

        public PixelFormat Format
        {
            get
            {
                return new PixelFormat((IntPtr)ptr->format, false);
            }
        }

        public int Pitch => ptr->pitch;

        public Rect Clip
        {
            get
            {
                Rect result;
                SDL_GetClipRect(this, out result);
                return result;
            }
            set
            {
                SDL_SetClipRect(this, &value);
            }
        }

        public BlendMode BlendMode
        {
            get
            {
                BlendMode mode;
                ErrorIfNegative(SDL_GetSurfaceBlendMode(this, out mode));
                return mode;
            }
            set
            {
                ErrorIfNegative(SDL_SetSurfaceBlendMode(this, value));
            }
        }

        public uint? ColorKey
        {
            get
            {
                uint key;
                int res = SDL_GetColorKey(this, out key);
                if (res == -1)
                    return null;
                ErrorIfNegative(res);
                return key;
            }
            set
            {
                if (value == null)
                    SDL_SetColorKey(this, 0, 0);
                else
                    SDL_SetColorKey(this, 1, value.Value);
            }
        }

        public byte AlphaMod
        {
            get
            {
                byte alpha;
                ErrorIfNegative(SDL_GetSurfaceAlphaMod(this, out alpha));
                return alpha;
            }
            set
            {
                ErrorIfNegative(SDL_SetSurfaceAlphaMod(this, value));
            }
        }

        public Color ColorMod
        {
            get
            {
                byte r, g, b;
                ErrorIfNegative(SDL_GetSurfaceColorMod(this, out r, out g, out b));
                return new Color(r, g, b);
            }
            set
            {
                ErrorIfNegative(SDL_SetSurfaceColorMod(this, value.r, value.g, value.b));
            }
        }

        public static void Blit(
          Surface src,
          Surface dst
        )
        {
            ErrorIfNegative(SDL_BlitSurface(src, null, dst, null));
        }

        public static void Blit(
          Surface src,
          in Rect srcRect,
          Surface dst
        )
        {
            fixed (Rect* srcrect = &srcRect)
                ErrorIfNegative(SDL_BlitSurface(src, srcrect, dst, null));
        }

        public static void Blit(
          Surface src,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_BlitSurface(src, null, dst, dstrect));
        }

        public static void Blit(
          Surface src,
          in Rect srcRect,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* srcrect = &srcRect)
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_BlitSurface(src, srcrect, dst, dstrect));
        }

        public static void BlitScaled(
          Surface src,
          Surface dst
        )
        {
            ErrorIfNegative(SDL_BlitScaled(src, null, dst, null));
        }

        public static void BlitScaled(
          Surface src,
          in Rect srcRect,
          Surface dst
        )
        {
            fixed (Rect* srcrect = &srcRect)
                ErrorIfNegative(SDL_BlitScaled(src, srcrect, dst, null));
        }

        public static void BlitScaled(
          Surface src,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_BlitScaled(src, null, dst, dstrect));
        }

        public static void BlitScaled(
          Surface src,
          in Rect srcRect,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* srcrect = &srcRect)
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_BlitScaled(src, srcrect, dst, dstrect));
        }

        public static void LowerBlit(
          Surface src,
          Surface dst
        )
        {
            ErrorIfNegative(SDL_LowerBlit(src, null, dst, null));
        }

        public static void LowerBlit(
          Surface src,
          in Rect srcRect,
          Surface dst
        )
        {
            fixed (Rect* srcrect = &srcRect)
                ErrorIfNegative(SDL_LowerBlit(src, srcrect, dst, null));
        }

        public static void LowerBlit(
          Surface src,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_LowerBlit(src, null, dst, dstrect));
        }

        public static void LowerBlit(
          Surface src,
          in Rect srcRect,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* srcrect = &srcRect)
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_LowerBlit(src, srcrect, dst, dstrect));
        }

        public static void LowerBlitScaled(
          Surface src,
          Surface dst
        )
        {
            ErrorIfNegative(SDL_LowerBlitScaled(src, null, dst, null));
        }

        public static void LowerBlitScaled(
          Surface src,
          in Rect srcRect,
          Surface dst
        )
        {
            fixed (Rect* srcrect = &srcRect)
                ErrorIfNegative(SDL_LowerBlitScaled(src, srcrect, dst, null));
        }

        public static void LowerBlitScaled(
          Surface src,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_LowerBlitScaled(src, null, dst, dstrect));
        }

        public static void LowerBlitScaled(
          Surface src,
          in Rect srcRect,
          Surface dst,
          in Rect dstRect
        )
        {
            fixed (Rect* srcrect = &srcRect)
            fixed (Rect* dstrect = &dstRect)
                ErrorIfNegative(SDL_LowerBlitScaled(src, srcrect, dst, dstrect));
        }

        public void Fill(uint color)
        {
            ErrorIfNegative(SDL_FillRect(this, null, color));
        }

        public void Fill(in Rect rect, uint color)
        {
            fixed (Rect* rp = &rect)
                ErrorIfNegative(SDL_FillRect(this, rp, color));
        }

        public void Fill(ReadOnlySpan<Rect> rects, uint color)
        {
            fixed (Rect* rp = &MemoryMarshal.GetReference(rects))
                ErrorIfNegative(SDL_FillRects(this, rp, rects.Length, color));
        }

        public Surface Convert(uint pixelFormat)
        {
            return ErrorIfInvalid(SDL_ConvertSurfaceFormat(this, pixelFormat, 0));
        }

        public Surface Convert(PixelFormat pixelFormat)
        {
            return ErrorIfInvalid(SDL_ConvertSurface(this, pixelFormat, 0));
        }

        public bool MustLock()
        {
            return ptr->flags.HasFlag(SurfaceFlags.RLEAccelerated);
        }

        public void SetRLE(bool enabled)
        {
            ErrorIfNegative(SDL_SetSurfaceRLE(this, enabled ? 1 : 0));
        }

        public void SetPalette(Palette palette)
        {
            ErrorIfNegative(SDL_SetSurfacePalette(this, palette));
        }

        public static Surface Create(
            int width,
            int height,
            int depth,
            uint rmask,
            uint gmask,
            uint bmask,
            uint amask)
        {
            return ErrorIfInvalid(SDL_CreateRGBSurface(0, width, height, depth, rmask, gmask, bmask, amask));
        }

        public static Surface Create(
            int width,
            int height,
            int depth,
            uint format)
        {
            return ErrorIfInvalid(SDL_CreateRGBSurfaceWithFormat(0, width, height, depth, format));
        }

        public static Surface From(
            ReadOnlySpan<byte> data,
            int width,
            int height,
            in PixelFormatMask mask,
            int pitch)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(data))
                return ErrorIfInvalid(SDL_CreateRGBSurfaceFrom(ptr, width, height, mask.BitsPerPixel, pitch, mask.R, mask.G, mask.B, mask.A));
        }

        public static Surface From(
            ReadOnlySpan<byte> data,
            int width,
            int height,
            int depth,
            int pitch,
            uint format)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(data))
                return ErrorIfInvalid(SDL_CreateRGBSurfaceWithFormatFrom(ptr, width, height, depth, pitch, format));
        }

        public static Surface LoadBMP(string file)
        {
            return ErrorIfInvalid(SDL_LoadBMP_RW(RWOps.FromFile(file, "rb"), 1));
        }

        public static Surface LoadBMP(RWOps src)
        {
            return ErrorIfInvalid(SDL_LoadBMP_RW(src, 0));
        }

        public void SaveBMP(string file)
        {
            ErrorIfNegative(SDL_SaveBMP_RW(this, RWOps.FromFile(file, "wb"), 1));
        }

        public void SaveBMP(RWOps dst)
        {
            ErrorIfNegative(SDL_SaveBMP_RW(this, dst, 0));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            SDL_FreeSurface(this.handle);
            return true;
        }

        public override string ToString()
        {
            return $"Surface <{Width}x{Height},Format={Format}>";
        }


        public SurfacePixels Pixels => new SurfacePixels(this);

        public class SurfacePixels : IDisposable
        {
            readonly Surface surface;
            readonly bool wasLocked;
            bool disposed;

            internal SurfacePixels(Surface surface)
            {
                this.wasLocked = surface.ptr->flags.HasFlag(SurfaceFlags.RLEAccelerated);
                this.surface = surface;
                if (wasLocked)
                    ErrorIfNegative(SDL_LockSurface(surface));
            }

            public Span<byte> Get()
            {
                int len = surface.Format.BytesPerPixel * surface.Width * surface.Height;
                return new Span<byte>(surface.ptr->pixels, len);
            }

            public void Dispose()
            {
                if (wasLocked && !disposed)
                    SDL_UnlockSurface(surface);
                disposed = true;
            }
        }
    }
}
