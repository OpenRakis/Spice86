using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public unsafe class AudioStream : IDisposable
    {
        readonly AudioStreamFormat format;
        IntPtr buffer;
        readonly uint length;

        protected byte* ptr
        {
            get
            {
                if (buffer == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(AudioStream));
                return (byte*)ptr;
            }
        }

        public AudioStream(AudioStreamFormat format, IntPtr buffer, uint length)
        {
            this.format = format;
            this.buffer = buffer;
            this.length = length;
        }

        public AudioStreamFormat Format => format;

        public uint Length => length;

        public Span<byte> AsSpan()
          => new Span<byte>(ptr, (int)length);

        public ReadOnlySpan<byte> AsReadOnlySpan()
          => new ReadOnlySpan<byte>(ptr, (int)length);

        public void Dispose()
        {
            byte* ptr = (byte*)System.Threading.Interlocked.Exchange(ref buffer, IntPtr.Zero);
            if (ptr != null)
                SDL_FreeWAV(ptr);
        }
    }
}

