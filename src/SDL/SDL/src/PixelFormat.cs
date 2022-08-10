using System;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public unsafe class PixelFormat : SafeHandle
    {

        private SDL_PixelFormat* ptr
        {
            get
            {
                if (IsInvalid) throw new ObjectDisposedException(nameof(PixelFormat));
                return (SDL_PixelFormat*)handle;
            }
        }

        private PixelFormat() : base(IntPtr.Zero, true)
        {
        }

        internal PixelFormat(IntPtr ptr, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(ptr);
        }

        public PixelFormat(PixelDataFormat dataFormat) : this((uint)dataFormat) { }
        public PixelFormat(uint dataFormat) : this()
        {
            PixelFormat? format = ErrorIfInvalid(SDL_AllocFormat(dataFormat));
            SetHandle(format.handle);
            format.SetHandle(IntPtr.Zero);
        }

        public PixelFormat(PixelFormatMask mask) : this(mask.DataFormat) { }

        public PixelDataFormat DataFormat => (PixelDataFormat)ptr->format;

        public Palette Palette
        {
            get
            {
                return new Palette((IntPtr)ptr->palette, false);
            }
            set
            {
                ErrorIfNegative(SDL_SetPixelFormatPalette(this, value));
            }
        }

        public byte BitsPerPixel => ptr->BitsPerPixel;

        public byte BytesPerPixel => ptr->BytesPerPixel;

        public uint RMask => ptr->Rmask;
        public uint GMask => ptr->Gmask;
        public uint BMask => ptr->Bmask;
        public uint AMask => ptr->Amask;

        public void SetPalette(Palette palette)
        {
            ErrorIfNegative(SDL_SetPixelFormatPalette(this, palette));
        }

        public uint Encode(byte r, byte g, byte b)
        {
            return SDL_MapRGB(this, r, g, b);
        }

        public uint Encode(byte r, byte g, byte b, byte a)
        {
            return SDL_MapRGBA(this, r, g, b, a);
        }

        public uint Encode(Color clr)
        {
            return SDL_MapRGBA(this, clr.r, clr.g, clr.b, clr.a);
        }

        public Color Decode(uint pixel)
        {
            byte r, g, b, a;
            SDL_GetRGBA(pixel, this, out r, out g, out b, out a);
            return new Color(r, g, b, a);
        }

        public void Decode(uint pixel, out byte r, out byte g, out byte b)
        {
            SDL_GetRGB(pixel, this, out r, out g, out b);
        }

        public void Decode(uint pixel, out byte r, out byte g, out byte b, out byte a)
        {
            SDL_GetRGBA(pixel, this, out r, out g, out b, out a);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_FreeFormat(this.handle);
            return true;
        }

        public override string ToString()
        {
            return GetName(this.DataFormat);
        }

        public static string GetName(uint dataFormat)
        {
            return UTF8ToString(SDL_GetPixelFormatName(dataFormat)) ?? ((PixelDataFormat)dataFormat).ToString();
        }
        public static string GetName(PixelDataFormat dataFormat)
        {
            return GetName((uint)dataFormat);
        }

        public static void Convert(
          int width, int height,
          uint srcFormat,
          ReadOnlySpan<byte> src,
          int srcPitch,
          uint dstFormat,
          Span<Byte> dst,
          int dstPitch
        )
        {
            fixed (byte* srcp = &MemoryMarshal.GetReference(src))
            fixed (byte* dstp = &MemoryMarshal.GetReference(dst))
                ErrorIfNegative(SDL_ConvertPixels(width, height, srcFormat, srcp, srcPitch, dstFormat, dstp, dstPitch));
        }
    }
}
