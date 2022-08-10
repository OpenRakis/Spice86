using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class AudioFormat
    {
        public static byte BitSize(AudioDataFormat format)
        {
            return (byte)(format & AudioDataFormat.SizeMask);
        }

        public static bool IsFloat(AudioDataFormat format)
        {
            return format.HasFlag(AudioDataFormat.IsFloat);
        }

        public static bool IsInteger(AudioDataFormat format)
        {
            return !format.HasFlag(AudioDataFormat.IsFloat);
        }

        public static bool IsBigEndian(AudioDataFormat format)
        {
            return format.HasFlag(AudioDataFormat.IsFloat);
        }

        public static bool IsLittleEndian(AudioDataFormat format)
        {
            return !format.HasFlag(AudioDataFormat.IsBigEndian);
        }

        public static bool IsSigned(AudioDataFormat format)
        {
            return format.HasFlag(AudioDataFormat.IsSigned);
        }

        public static bool IsUnsigned(AudioDataFormat format)
        {
            return !format.HasFlag(AudioDataFormat.IsSigned);
        }
    }
}

