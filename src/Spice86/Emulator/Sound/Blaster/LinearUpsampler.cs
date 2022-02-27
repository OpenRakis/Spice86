namespace Spice86.Emulator.Sound.Blaster;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// An adequate but not great audio resampler.
/// </summary>
internal static class LinearUpsampler
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Resample8Mono(int sourceRate, int destRate, ReadOnlySpan<byte> source, Span<short> dest)
    {
        double src2Dest = (double)destRate / (double)sourceRate;
        double dest2Src = (double)sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length);

        for (int i = 0; i < length; i++)
        {
            int srcIndex = (int)(i * dest2Src);
            var remainder = (i * dest2Src) % 1;

            var value1 = Convert8To16(source[srcIndex]);
            if (srcIndex < source.Length - 1)
            {
                var value2 = Convert8To16(source[srcIndex + 1]);

                var newValue = Interpolate(value1, value2, remainder);
                dest[i << 1] = newValue;
                dest[(i << 1) + 1] = newValue;
            }
            else
            {
                dest[i << 1] = value1;
                dest[(i << 1) + 1] = value1;
            }
        }

        return length * 2;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Resample8Stereo(int sourceRate, int destRate, ReadOnlySpan<byte> source, Span<short> dest)
    {
        double src2Dest = (double)destRate / (double)sourceRate;
        double dest2Src = (double)sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length) / 2;

        for (int i = 0; i < length; i++)
        {
            int srcIndex = (int)(i * dest2Src) << 1;

            var value1Left = Convert8To16(source[srcIndex]);
            var value1Right = Convert8To16(source[srcIndex + 1]);
            if (srcIndex < source.Length - 3)
            {
                var remainder = (i * dest2Src) % 1;
                var value2Left = Convert8To16(source[srcIndex + 2]);
                var value2Right = Convert8To16(source[srcIndex + 3]);

                dest[i << 1] = Interpolate(value1Left, value2Left, remainder);
                dest[(i << 1) + 1] = Interpolate(value1Right, value2Right, remainder);
            }
            else
            {
                dest[i << 1] = value1Left;
                dest[(i << 1) + 1] = value1Right;
            }
        }

        return length * 2;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Resample16Mono(int sourceRate, int destRate, ReadOnlySpan<short> source, Span<short> dest)
    {
        double src2Dest = (double)destRate / (double)sourceRate;
        double dest2Src = (double)sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length);

        for (int i = 0; i < length; i++)
        {
            int srcIndex = (int)(i * dest2Src);
            var remainder = (i * dest2Src) % 1;

            var value1 = source[srcIndex];
            if (srcIndex < source.Length - 1)
            {
                var value2 = source[srcIndex + 1];

                var newValue = Interpolate(value1, value2, remainder);
                dest[i << 1] = newValue;
                dest[(i << 1) + 1] = newValue;
            }
            else
            {
                dest[i << 1] = value1;
                dest[(i << 1) + 1] = value1;
            }
        }

        return length * 2;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Resample16Stereo(int sourceRate, int destRate, ReadOnlySpan<short> source, Span<short> dest)
    {
        double src2Dest = (double)destRate / (double)sourceRate;
        double dest2Src = (double)sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length) / 2;

        for (int i = 0; i < length; i++)
        {
            int srcIndex = (int)(i * dest2Src) << 1;

            var value1Left = source[srcIndex];
            var value1Right = source[srcIndex + 1];
            if (srcIndex < source.Length - 3)
            {
                var remainder = (i * dest2Src) % 1;
                var value2Left = source[srcIndex + 2];
                var value2Right = source[srcIndex + 3];

                dest[i << 1] = Interpolate(value1Left, value2Left, remainder);
                dest[(i << 1) + 1] = Interpolate(value1Right, value2Right, remainder);
            }
            else
            {
                dest[i << 1] = value1Left;
                dest[(i << 1) + 1] = value1Right;
            }
        }

        return length * 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short Interpolate(short a, short b, double factor) => (short)(((b - a) * factor) + a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short Convert8To16(byte s) => (short)((s - 128) << 8);
}
