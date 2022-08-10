using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class AudioStreamFormat
    {
        internal SDL_AudioSpec spec;

        public int Frequency
        {
            get => spec.freq;
            set => spec.freq = value;
        }

        public AudioDataFormat Format
        {
            get => spec.format;
            set => spec.format = value;
        }

        public byte Channels
        {
            get => spec.channels;
            set => spec.channels = value;
        }

        public byte SilenceValue
        {
            get => spec.silence;
            set => spec.silence = value;
        }

        public uint BufferSize
        {
            get => spec.size;
            set => spec.size = value;
        }

        public unsafe AudioStreamFormat(
          int frequency,
          AudioDataFormat format,
          byte channels,
          byte silenceValue,
          uint bufferSize
        )
        {
            spec.freq = frequency;
            spec.format = format;
            spec.channels = channels;
            spec.silence = silenceValue;
            spec.size = bufferSize;
            spec.userdata = null;
            spec.callback = IntPtr.Zero;
        }

        internal AudioStreamFormat(SDL_AudioSpec spec)
        {
            this.spec = spec;
        }

        public override string ToString()
        {
            return $"{nameof(AudioStreamFormat)} [Frequency={Frequency},Format={Format},Channels={Channels},Silence={SilenceValue},BufferSize={BufferSize}]";
        }
    }
}
