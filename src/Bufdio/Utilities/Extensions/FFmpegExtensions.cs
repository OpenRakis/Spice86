using System;
using System.Runtime.InteropServices;
using Bufdio.Exceptions;
using FFmpeg.AutoGen;

namespace Bufdio.Utilities.Extensions;

internal static class FFmpegExtensions
{
    private const int ErrorBufferSize = 1024;

    public static bool FFIsEOF(this int code)
    {
        return code == ffmpeg.AVERROR_EOF;
    }

    public static bool FFIsError(this int code)
    {
        return code < 0;
    }

    public static int FFGuard(this int code)
    {
        if (!code.FFIsError())
        {
            return code;
        }

        throw new FFmpegException(code);
    }

    public static string FFErrorToText(this int code)
    {
        unsafe
        {
            var buffer = stackalloc byte[ErrorBufferSize];
            ffmpeg.av_strerror(code, buffer, ErrorBufferSize);

            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }
    }
}
