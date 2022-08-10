using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MixerChunk : SafeHandle
    {
        public static MixerChunkDecoders Decoders { get; } = new MixerChunkDecoders();

        internal unsafe Mix_Chunk* ptr
        {
            get
            {
                if (IsInvalid)
                    throw new ObjectDisposedException(nameof(MixerChannelGroup));
                return (Mix_Chunk*)handle;
            }
        }

        protected MixerChunk() : base(IntPtr.Zero, true)
        {
        }

        public MixerChunk(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public int Volume
        {
            get
            {
                return Mix_VolumeChunk(this, -1);
            }
            set
            {
                Mix_VolumeChunk(this, value);
            }
        }

        public unsafe uint Length => ptr->alen;

        public static MixerChunk Load(string file)
        {
            return Mix_LoadWAV_RW(RWOps.FromFile(file, "rb"), 1);
        }

        public static MixerChunk Load(RWOps ops)
        {
            return Mix_LoadWAV_RW(ops, 0);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            Mix_FreeChunk(this.handle);
            return true;
        }
    }
}
