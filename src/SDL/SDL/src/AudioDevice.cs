using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class AudioDevice : IDisposable
    {
        readonly protected uint id;
        readonly bool owned;
        int disposed = 0;

        internal AudioDevice(uint id, bool owned)
        {
            this.id = id;
            this.owned = owned;
            if (!owned)
                GC.SuppressFinalize(this);
        }

        public AudioStatus Status
        {
            get
            {
                return SDL_GetAudioDeviceStatus(id);
            }
        }

        public bool Paused
        {
            get
            {
                return Status != AudioStatus.Playing;
            }
            set
            {
                SDL_PauseAudioDevice(id, value ? 1 : 0);
            }
        }

        public uint QueueSize => SDL_GetQueuedAudioSize(id);

        public void ClearQueue()
        {
            SDL_ClearQueuedAudio(id);
        }

        ~AudioDevice()
        {
            Dispose(false);
        }

        protected void Dispose(bool disposing)
        {
            int closed = System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0);
            if (closed == 0)
                SDL_CloseAudioDevice(this.id);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public override string ToString()
        {
            return $"AudioDevice{id} ({Status},QueueSize={QueueSize})";
        }
    }

    public class AudioInputDevice : AudioDevice
    {
        internal AudioInputDevice(uint id, bool owned) : base(id, owned) { }

        public unsafe int Dequeue(Span<byte> buffer)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
                return (int)SDL_DequeueAudio(id, ptr, (uint)buffer.Length);
        }

        public override string ToString()
        {
            return base.ToString().Replace("AudioDevice", "AudioInputDevice");
        }
    }

    public class AudioOutputDevice : AudioDevice
    {
        internal AudioOutputDevice(uint id, bool owned) : base(id, owned) { }

        public unsafe void Enqueue(ReadOnlySpan<byte> buffer)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
                ErrorIfNegative(SDL_QueueAudio(id, ptr, (uint)buffer.Length));
        }

        public override string ToString()
        {
            return base.ToString().Replace("AudioDevice", "AudioOutputDevice");
        }
    }
}
