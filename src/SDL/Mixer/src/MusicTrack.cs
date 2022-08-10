using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MusicTrack : SafeHandle
    {
        public static MusicDecoders Decoders { get; } = new MusicDecoders();

        protected MusicTrack() : base(IntPtr.Zero, true)
        {
        }

        public MusicTrack(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public MusicType Type => Mix_GetMusicType(this);


        public static unsafe MusicTrack Load(string file)
        {
            Span<byte> b = stackalloc byte[SL(file)];
            StringToUTF8(file, b);
            fixed (byte* f = b)
                return ErrorIfInvalid(Mix_LoadMUS(f));
        }

        public static MusicTrack Load(RWOps ops)
        {
            return ErrorIfInvalid(Mix_LoadMUS_RW(ops, 0));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            Mix_FreeMusic(this.handle);
            return true;
        }

        public override string ToString()
        {
            return $"MusicTrack({Type})";
        }
    }
}
