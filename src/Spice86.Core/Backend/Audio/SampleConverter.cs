namespace Spice86.Core.Backend.Audio;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Converts between various sample formats.
/// </summary>
internal static class SampleConverter {
    /// <summary>
    /// Converts 16-bit PCM samples to 32-bit IEEE float samples.
    /// </summary>
    /// <param name="source">Source samples.</param>
    /// <param name="target">Target sample buffer.</param>
    private static void Pcm16ToFloat(Span<short> source, Span<float> target) {
        if (Vector.IsHardwareAccelerated) {
            Span<Vector<short>> srcVector = MemoryMarshal.Cast<short, Vector<short>>(source);
            Span<Vector<float>> destVector = MemoryMarshal.Cast<float, Vector<float>>(target);

            for (int i = 0; i < srcVector.Length; i++) {
                int targetIndex = i * 2;
                Vector.Widen(srcVector[i], out Vector<int> iSrc1, out Vector<int> iSrc2);
                destVector[targetIndex] = Vector.Multiply(Vector.ConvertToSingle(iSrc1), 1f / 32768);
                destVector[targetIndex + 1] = Vector.Multiply(Vector.ConvertToSingle(iSrc2), 1f / 32768);
            }

            for (int i = srcVector.Length * Vector<short>.Count; i < source.Length; i++) {
                target[i] = source[i] / 32768f;
            }
        } else {
            for (int i = 0; i < source.Length; i++) {
                target[i] = source[i] / 32768f;
            }
        }
    }
    /// <summary>
    /// Converts 8-bit PCM samples to 32-bit IEEE float samples.
    /// </summary>
    /// <param name="source">Source samples.</param>
    /// <param name="target">Target sample buffer.</param>
    private static void Pcm8ToFloat(Span<byte> source, Span<float> target) {
        if (Vector.IsHardwareAccelerated) {
            Span<Vector<byte>> srcVector = source.Cast<byte, Vector<byte>>();
            Span<Vector<float>> destVector = target.Cast<float, Vector<float>>();
            Vector<short> addVector = new Vector<short>(-127);

            for (int i = 0; i < srcVector.Length; i++) {
                int targetIndex = i * 4;
                Vector.Widen(srcVector[i], out Vector<ushort> usSrc1, out Vector<ushort> usSrc2);

                Vector<short> sSrc1 = Vector.Add(Vector.AsVectorInt16(usSrc1), addVector);
                Vector<short> sSrc2 = Vector.Add(Vector.AsVectorInt16(usSrc2), addVector);

                Vector.Widen(sSrc1, out Vector<int> iSrc1, out Vector<int> iSrc2);
                Vector.Widen(sSrc2, out Vector<int> iSrc3, out Vector<int> iSrc4);

                destVector[targetIndex] = Vector.Multiply(Vector.ConvertToSingle(iSrc1), 1f / 128);
                destVector[targetIndex + 1] = Vector.Multiply(Vector.ConvertToSingle(iSrc2), 1f / 128);
                destVector[targetIndex + 2] = Vector.Multiply(Vector.ConvertToSingle(iSrc3), 1f / 128);
                destVector[targetIndex + 3] = Vector.Multiply(Vector.ConvertToSingle(iSrc4), 1f / 128);
            }

            for (int i = srcVector.Length * Vector<byte>.Count; i < source.Length; i++) {
                target[i] = (source[i] - 127) / 128f;
            }
        } else {
            for (int i = 0; i < source.Length; i++) {
                target[i] = (source[i] - 127) / 128f;
            }
        }
    }

    /// <summary>
    /// Converts 8-bit PCM samples to 16-bit IEEE float samples.
    /// </summary>
    /// <param name="source">Source samples.</param>
    /// <param name="target">Target sample buffer.</param>
    private static void Pcm8ToPcm16(Span<byte> source, Span<short> target) {
        if (Vector.IsHardwareAccelerated) {
            Span<Vector<byte>> srcVector = source.Cast<byte, Vector<byte>>();
            Span<Vector<short>> destVector = target.Cast<short, Vector<short>>();
            Vector<short> addVector = new Vector<short>(-127);

            for (int i = 0; i < srcVector.Length; i++) {
                int targetIndex = i * 2;
                Vector.Widen(srcVector[i], out Vector<ushort> usSrc1, out Vector<ushort> usSrc2);

                Vector<short> sSrc1 = Vector.Multiply(Vector.Add(Vector.AsVectorInt16(usSrc1), addVector), (short)128);
                Vector<short> sSrc2 = Vector.Multiply(Vector.Add(Vector.AsVectorInt16(usSrc2), addVector), (short)128);

                destVector[targetIndex] = Vector.Multiply(sSrc1, (short)256);
                destVector[targetIndex + 1] = Vector.Multiply(sSrc2, (short)256);
            }

            for (int i = srcVector.Length * Vector<byte>.Count; i < source.Length; i++) {
                target[i] = (short)((source[i] - 127) * 256);
            }
        } else {
            for (int i = 0; i < source.Length; i++) {
                target[i] = (short)((source[i] - 127) * 256);
            }
        }
    }
    /// <summary>
    /// Converts 32-bit IEEE float samples tp 16-bit PCM samples.
    /// </summary>
    /// <param name="source">Source samples.</param>
    /// <param name="target">Target sample buffer.</param>
    private static void FloatToPcm16(Span<float> source, Span<short> target) {
        if (Vector.IsHardwareAccelerated) {
            Span<Vector<float>> srcVector = source.Cast<float, Vector<float>>();
            Span<Vector<short>> destVector = target.Cast<short, Vector<short>>();

            for (int i = 0; i < srcVector.Length - 1; i += 2) {
                int targetIndex = i / 2;

                Vector<int> src1 = Vector.ConvertToInt32(Vector.Multiply(srcVector[i], 32767f));
                Vector<int> src2 = Vector.ConvertToInt32(Vector.Multiply(srcVector[i + 1], 32767f));

                destVector[targetIndex] = Vector.Narrow(src1, src2);
            }

            for (int i = srcVector.Length * Vector<float>.Count; i < source.Length; i++) {
                target[i] = (short)(source[i] * 32767f);
            }
        } else {
            for (int i = 0; i < source.Length; i++) {
                target[i] = (short)(source[i] * 32767f);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void InternalConvert<TFrom, TTo>(Span<TFrom> source, Span<TTo> target)
        where TFrom : unmanaged
        where TTo : unmanaged {
        if (typeof(TFrom) == typeof(short) && typeof(TTo) == typeof(float)) {
            Pcm16ToFloat(source.Cast<TFrom, short>(), target.Cast<TTo, float>());
        } else if (typeof(TFrom) == typeof(byte) && typeof(TTo) == typeof(short)) {
            Pcm8ToPcm16(source.Cast<TFrom, byte>(), target.Cast<TTo, short>());
        } else if (typeof(TFrom) == typeof(float) && typeof(TTo) == typeof(short)) {
            FloatToPcm16(source.Cast<TFrom, float>(), target.Cast<TTo, short>());
        } else if (typeof(TFrom) == typeof(byte) && typeof(TTo) == typeof(float)) {
            Pcm8ToFloat(source.Cast<TFrom, byte>(), target.Cast<TTo, float>());
        } else {
            throw new NotImplementedException();
        }
    }
}
