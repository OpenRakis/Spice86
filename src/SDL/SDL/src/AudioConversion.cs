using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class AudioConversion
    {
        SDL_AudioCVT conversion;
        readonly int needed;
        readonly object locker = new object();

        public AudioConversion(
          AudioDataFormat sourceFormat,
          byte sourceChannels,
          int sourceRate,
          AudioDataFormat destinationFormat,
          byte destinationChannels,
          int destinationRate
        )
        {
            needed = ErrorIfNegative(SDL_BuildAudioCVT(
              out conversion,
              sourceFormat,
              sourceChannels,
              sourceRate,
              destinationFormat,
              destinationChannels,
              destinationRate));
        }

        public AudioDataFormat SourceFormat => conversion.src_format;
        public AudioDataFormat DestinationFormat => conversion.dst_format;
        public double RateConversionIncrement => conversion.rate_incr;
        public double LengthRatio => conversion.len_ratio;
        public bool IsNeeded => needed != 0;

        public int GetRequiredBufferSize(int sourceLength)
        {
            return (int)Math.Ceiling(sourceLength * conversion.len_ratio);
        }

        public unsafe int Convert(Span<byte> span, int length)
        {
            if (!IsNeeded)
                return length;

            if (span.Length < GetRequiredBufferSize(length))
                throw new ArgumentException("A span with a length greater than or equal to GetRequiredBufferSize(length) is required", nameof(span));

            lock (locker)
            {
                fixed (byte* buf = &MemoryMarshal.GetReference(span))
                {
                    conversion.buf = buf;
                    conversion.len = length;
                    ErrorIfNegative(SDL_ConvertAudio(ref conversion));
                    conversion.buf = null;
                    conversion.len = 0;
                    return conversion.len_cvt;
                }
            }
        }
    }
}
