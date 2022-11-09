namespace Spice86.Core.Emulator.Sound.Blaster;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// An adequate but not great audio resampler.
/// </summary>
internal static class LinearUpsampler {
    public static int Resample8Mono(int sourceRate, int destRate, ReadOnlySpan<byte> source, Span<short> dest) {
        double src2Dest = destRate / (double)sourceRate;
        double dest2Src = sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length);

        for (int i = 0; i < length; i++) {
            int srcIndex = (int)(i * dest2Src);
            double remainder = i * dest2Src % 1;

            short value1 = Convert8To16(source[srcIndex]);
            if (srcIndex < source.Length - 1) {
                short value2 = Convert8To16(source[srcIndex + 1]);

                short newValue = Interpolate(value1, value2, remainder);
                dest[i << 1] = newValue;
                dest[(i << 1) + 1] = newValue;
            } else {
                dest[i << 1] = value1;
                dest[(i << 1) + 1] = value1;
            }
        }

        return length * 2;
    }

    public static int Resample8Stereo(int sourceRate, int destRate, ReadOnlySpan<byte> source, Span<short> dest) {
        double src2Dest = destRate / (double)sourceRate;
        double dest2Src = sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length) / 2;

        for (int i = 0; i < length; i++) {
            int srcIndex = (int)(i * dest2Src) << 1;

            short value1Left = Convert8To16(source[srcIndex]);
            short value1Right = Convert8To16(source[srcIndex + 1]);
            if (srcIndex < source.Length - 3) {
                double remainder = i * dest2Src % 1;
                short value2Left = Convert8To16(source[srcIndex + 2]);
                short value2Right = Convert8To16(source[srcIndex + 3]);

                dest[i << 1] = Interpolate(value1Left, value2Left, remainder);
                dest[(i << 1) + 1] = Interpolate(value1Right, value2Right, remainder);
            } else {
                dest[i << 1] = value1Left;
                dest[(i << 1) + 1] = value1Right;
            }
        }

        return length * 2;
    }

    public static int Resample16Mono(int sourceRate, int destRate, ReadOnlySpan<short> source, Span<short> dest) {
        double src2Dest = destRate / (double)sourceRate;
        double dest2Src = sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length);

        for (int i = 0; i < length; i++) {
            int srcIndex = (int)(i * dest2Src);
            double remainder = i * dest2Src % 1;

            short value1 = source[srcIndex];
            if (srcIndex < source.Length - 1) {
                short value2 = source[srcIndex + 1];

                short newValue = Interpolate(value1, value2, remainder);
                dest[i << 1] = newValue;
                dest[(i << 1) + 1] = newValue;
            } else {
                dest[i << 1] = value1;
                dest[(i << 1) + 1] = value1;
            }
        }

        return length * 2;
    }

    public static int Resample16Stereo(int sourceRate, int destRate, ReadOnlySpan<short> source, Span<short> dest) {
        double src2Dest = destRate / (double)sourceRate;
        double dest2Src = sourceRate / (double)destRate;

        int length = (int)(src2Dest * source.Length) / 2;

        for (int i = 0; i < length; i++) {
            int srcIndex = (int)(i * dest2Src) << 1;

            short value1Left = source[srcIndex];
            short value1Right = source[srcIndex + 1];
            if (srcIndex < source.Length - 3) {
                double remainder = i * dest2Src % 1;
                short value2Left = source[srcIndex + 2];
                short value2Right = source[srcIndex + 3];

                dest[i << 1] = Interpolate(value1Left, value2Left, remainder);
                dest[(i << 1) + 1] = Interpolate(value1Right, value2Right, remainder);
            } else {
                dest[i << 1] = value1Left;
                dest[(i << 1) + 1] = value1Right;
            }
        }

        return length * 2;
    }


    private static short Interpolate(short a, short b, double factor) => (short)(((b - a) * factor) + a);

    private static short Convert8To16(byte s) => (short)(s - 128 << 8);
}
