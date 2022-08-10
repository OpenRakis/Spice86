using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class Texture : SafeHandle
    {
        protected Texture() : base(IntPtr.Zero, true)
        {
        }

        public Texture(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public unsafe int Width
        {
            get
            {
                int w;
                ErrorIfNegative(SDL_QueryTexture(this, IntPtr.Zero, IntPtr.Zero, out w, IntPtr.Zero));
                return w;
            }
        }

        public unsafe int Height
        {
            get
            {
                int h;
                ErrorIfNegative(SDL_QueryTexture(this, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out h));
                return h;
            }
        }

        public unsafe System.Drawing.Size Size
        {
            get
            {
                int w, h;
                ErrorIfNegative(SDL_QueryTexture(this, IntPtr.Zero, IntPtr.Zero, out w, out h));
                return new System.Drawing.Size(w, h);
            }
        }

        public unsafe TextureAccess Access
        {
            get
            {
                TextureAccess access;
                ErrorIfNegative(SDL_QueryTexture(this, IntPtr.Zero, out access, IntPtr.Zero, IntPtr.Zero));
                return access;
            }
        }

        public unsafe uint Format
        {
            get
            {
                uint format;
                ErrorIfNegative(SDL_QueryTexture(this, out format, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                return format;
            }
        }

        public byte AlphaMod
        {
            get
            {
                byte alpha;
                ErrorIfNegative(SDL_GetTextureAlphaMod(this, out alpha));
                return alpha;
            }
            set
            {
                ErrorIfNegative(SDL_SetTextureAlphaMod(this, value));
            }
        }

        public Color ColorMod
        {
            get
            {
                byte r, g, b;
                ErrorIfNegative(SDL_GetTextureColorMod(this, out r, out g, out b));
                return new Color(r, g, b);
            }
            set
            {
                ErrorIfNegative(SDL_SetTextureColorMod(this, value.r, value.g, value.b));
            }
        }

        public BlendMode BlendMode
        {
            get
            {
                BlendMode mode;
                ErrorIfNegative(SDL_GetTextureBlendMode(this, out mode));
                return mode;
            }
            set
            {
                ErrorIfNegative(SDL_SetTextureBlendMode(this, value));
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            SDL_DestroyTexture(this.handle);
            return true;
        }

        public override string ToString()
        {
            int w, h;
            TextureAccess access;
            uint format;

            int err = SDL_QueryTexture(this, out format, out access, out w, out h);
            if (err < 0)
                return "Texture <unknown>";
            else
                return $"Texture <{w}x{h} {access},Format={PixelFormat.GetName(format)}>";
        }
    }
}
