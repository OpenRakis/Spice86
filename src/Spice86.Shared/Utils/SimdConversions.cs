namespace Spice86.Shared.Utils;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///     Provides helpers for converting and scaling sample buffers with SIMD intrinsics when available.
/// </summary>
public static class SimdConversions {
    /// <summary>
    ///     Converts signed 16-bit samples to single-precision floating point values scaled by the provided factor.
    /// </summary>
    /// <param name="src">Source span containing the signed 16-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="dst" /> is shorter than <paramref name="src" />.</exception>
    /// <remarks>
    ///     When <paramref name="scale" /> is zero the destination span is cleared for the length of the source.
    ///     SIMD acceleration is used if supported by the current runtime; otherwise a scalar fallback is executed.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void ConvertInt16ToScaledFloat(ReadOnlySpan<short> src, Span<float> dst, float scale) {
        if (src.Length > dst.Length) {
            throw new ArgumentException("Destination span is shorter than source.", nameof(dst));
        }

        if (src.IsEmpty) {
            return;
        }

        if (scale == 0f) {
            dst[..src.Length].Clear();
            return;
        }

        if (Vector.IsHardwareAccelerated) {
            ConvertInt16ToScaledFloat_Vector(src, dst, scale);
            return;
        }

        ConvertInt16ToScaledFloat_Scalar(src, dst, scale);
    }

    /// <summary>
    ///     Converts signed 16-bit samples to scaled single-precision floats using scalar operations.
    /// </summary>
    /// <param name="src">Source span containing the signed 16-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ConvertInt16ToScaledFloat_Scalar(ReadOnlySpan<short> src, Span<float> dst, float scale) {
        for (int i = 0; i < src.Length; i++) {
            dst[i] = src[i] * scale;
        }
    }

    /// <summary>
    ///     Converts signed 16-bit samples to scaled single-precision floats using the specified intrinsic backend.
    /// </summary>
    /// <param name="src">Source span containing the signed 16-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample.</param>
    /// <param name="backend">Intrinsic backend to force for testing purposes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="backend" /> is not supported.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static void ConvertInt16ToScaledFloatForTesting(ReadOnlySpan<short> src, Span<float> dst, float scale,
        IntrinsicBackend backend) {
        switch (backend) {
            case IntrinsicBackend.Vector:
                ConvertInt16ToScaledFloat_Vector(src, dst, scale);
                return;
            case IntrinsicBackend.Scalar:
                ConvertInt16ToScaledFloat_Scalar(src, dst, scale);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown intrinsic backend.");
        }
    }

    /// <summary>
    ///     Converts signed 16-bit samples to scaled single-precision floats using SIMD vector instructions when available.
    /// </summary>
    /// <param name="src">Source span containing the signed 16-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ConvertInt16ToScaledFloat_Vector(ReadOnlySpan<short> src, Span<float> dst, float scale) {
        int shortPerVec = Vector<short>.Count;
        int processed = 0;

        int fullLen = src.Length - (src.Length % shortPerVec);
        if (fullLen > 0) {
            var scaleVec = new Vector<float>(scale);

            ReadOnlySpan<Vector<short>> sVec = MemoryMarshal.Cast<short, Vector<short>>(src[..fullLen]);
            Span<Vector<float>> dVec = MemoryMarshal.Cast<float, Vector<float>>(dst[..fullLen]);

            for (int i = 0; i < sVec.Length; i++) {
                Vector<short> s = sVec[i];
                Vector.Widen(s, out Vector<int> lo32, out Vector<int> hi32);

                dVec[2 * i] = Vector.ConvertToSingle(lo32) * scaleVec;
                dVec[(2 * i) + 1] = Vector.ConvertToSingle(hi32) * scaleVec;
            }

            processed = fullLen;
        }

        ConvertInt16ToScaledFloat_Scalar(src[processed..], dst[processed..], scale);
    }

    /// <summary>
    ///     Converts unsigned 8-bit samples to single-precision floating point values scaled by the provided factor.
    /// </summary>
    /// <param name="src">Source span containing the unsigned 8-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied after the unsigned samples are centered around zero.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="dst" /> is shorter than <paramref name="src" />.</exception>
    /// <remarks>
    ///     Input samples are first converted to a signed representation by subtracting 127, then multiplied by
    ///     <paramref name="scale" /> / 128. SIMD acceleration is used if supported; otherwise a scalar fallback is executed.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void ConvertUInt8ToScaledFloat(ReadOnlySpan<byte> src, Span<float> dst, float scale) {
        if (src.Length > dst.Length) {
            throw new ArgumentException("Destination span is shorter than source.", nameof(dst));
        }

        if (src.IsEmpty) {
            return;
        }

        if (scale == 0f) {
            dst[..src.Length].Clear();
            return;
        }

        float normalizedScale = scale / 128f;

        if (Vector.IsHardwareAccelerated) {
            ConvertUInt8ToScaledFloat_Vector(src, dst, normalizedScale);
            return;
        }

        ConvertUInt8ToScaledFloat_Scalar(src, dst, normalizedScale);
    }

    /// <summary>
    ///     Converts unsigned 8-bit samples to scaled single-precision floats using scalar operations.
    /// </summary>
    /// <param name="src">Source span containing the unsigned 8-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample after centering.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ConvertUInt8ToScaledFloat_Scalar(ReadOnlySpan<byte> src, Span<float> dst, float scale) {
        for (int i = 0; i < src.Length; i++) {
            dst[i] = (src[i] - 127f) * scale;
        }
    }

    /// <summary>
    ///     Converts unsigned 8-bit samples to scaled single-precision floats using the specified intrinsic backend.
    /// </summary>
    /// <param name="src">Source span containing the unsigned 8-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample after centering.</param>
    /// <param name="backend">Intrinsic backend to force for testing purposes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="backend" /> is not supported.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static void ConvertUInt8ToScaledFloatForTesting(ReadOnlySpan<byte> src, Span<float> dst, float scale,
        IntrinsicBackend backend) {
        if (src.Length > dst.Length) {
            throw new ArgumentException("Destination span is shorter than source.", nameof(dst));
        }

        if (src.IsEmpty) {
            return;
        }

        if (scale == 0f) {
            dst[..src.Length].Clear();
            return;
        }

        float normalizedScale = scale / 128f;

        switch (backend) {
            case IntrinsicBackend.Vector:
                ConvertUInt8ToScaledFloat_Vector(src, dst, normalizedScale);
                return;
            case IntrinsicBackend.Scalar:
                ConvertUInt8ToScaledFloat_Scalar(src, dst, normalizedScale);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown intrinsic backend.");
        }
    }

    /// <summary>
    ///     Converts unsigned 8-bit samples to scaled single-precision floats using SIMD vector instructions when available.
    /// </summary>
    /// <param name="src">Source span containing the unsigned 8-bit samples to convert.</param>
    /// <param name="dst">Destination span that receives the scaled floating point samples.</param>
    /// <param name="scale">Scale factor applied to each converted sample after centering.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ConvertUInt8ToScaledFloat_Vector(ReadOnlySpan<byte> src, Span<float> dst, float scale) {
        int bytePerVec = Vector<byte>.Count;
        int processed = 0;

        int fullLen = src.Length - (src.Length % bytePerVec);
        if (fullLen > 0) {
            var scaleVec = new Vector<float>(scale);
            var offsetVec = new Vector<float>(127f);

            ReadOnlySpan<Vector<byte>> sVec = MemoryMarshal.Cast<byte, Vector<byte>>(src[..fullLen]);
            Span<Vector<float>> dVec = MemoryMarshal.Cast<float, Vector<float>>(dst[..fullLen]);

            ref Vector<float> destRef = ref MemoryMarshal.GetReference(dVec);
            int destIndex = 0;

            foreach (Vector<byte> s in sVec) {
                Vector.Widen(s, out Vector<ushort> lo16, out Vector<ushort> hi16);

                Vector.Widen(lo16, out Vector<uint> lo32A, out Vector<uint> lo32B);
                Vector.Widen(hi16, out Vector<uint> hi32A, out Vector<uint> hi32B);

                var f0 = Vector.ConvertToSingle(Vector.AsVectorInt32(lo32A));
                f0 -= offsetVec;
                f0 *= scaleVec;
                Unsafe.Add(ref destRef, destIndex++) = f0;

                var f1 = Vector.ConvertToSingle(Vector.AsVectorInt32(lo32B));
                f1 -= offsetVec;
                f1 *= scaleVec;
                Unsafe.Add(ref destRef, destIndex++) = f1;

                var f2 = Vector.ConvertToSingle(Vector.AsVectorInt32(hi32A));
                f2 -= offsetVec;
                f2 *= scaleVec;
                Unsafe.Add(ref destRef, destIndex++) = f2;

                var f3 = Vector.ConvertToSingle(Vector.AsVectorInt32(hi32B));
                f3 -= offsetVec;
                f3 *= scaleVec;
                Unsafe.Add(ref destRef, destIndex++) = f3;
            }

            processed = fullLen;
        }

        ConvertUInt8ToScaledFloat_Scalar(src[processed..], dst[processed..], scale);
    }

    /// <summary>
    ///     Scales the contents of the provided span by the supplied factor in place.
    /// </summary>
    /// <param name="data">Span containing the floating point values to scale.</param>
    /// <param name="scale">Scale factor applied to each element of <paramref name="data" />.</param>
    /// <remarks>
    ///     When <paramref name="scale" /> equals 1 the method returns immediately.
    ///     SIMD acceleration is used if supported by the current runtime; otherwise a scalar fallback is executed.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void ScaleInPlace(Span<float> data, float scale) {
        if (data.IsEmpty) {
            return;
        }

        switch (scale) {
            case 1f:
                return;
        }

        if (Vector.IsHardwareAccelerated) {
            ScaleInPlace_Vector(data, scale);
            return;
        }

        // Scalar fallback
        ScaleInPlace_Scalar(data, scale);
    }

    /// <summary>
    ///     Scales the contents of the provided span in place using the specified intrinsic backend.
    /// </summary>
    /// <param name="data">Span containing the floating point values to scale.</param>
    /// <param name="scale">Scale factor applied to each element of <paramref name="data" />.</param>
    /// <param name="backend">Intrinsic backend to force for testing purposes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="backend" /> is not supported.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static void ScaleInPlaceForTesting(Span<float> data, float scale, IntrinsicBackend backend) {
        switch (backend) {
            case IntrinsicBackend.Vector:
                ScaleInPlace_Vector(data, scale);
                return;
            case IntrinsicBackend.Scalar:
                ScaleInPlace_Scalar(data, scale);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown intrinsic backend.");
        }
    }

    /// <summary>
    ///     Scales the contents of the provided span by the supplied factor using scalar operations.
    /// </summary>
    /// <param name="data">Span containing the floating point values to scale.</param>
    /// <param name="scale">Scale factor applied to each element of <paramref name="data" />.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ScaleInPlace_Scalar(Span<float> data, float scale) {
        for (int i = 0; i < data.Length; i++) {
            data[i] *= scale;
        }
    }

    /// <summary>
    ///     Scales the contents of the provided span by the supplied factor using SIMD vector instructions when available.
    /// </summary>
    /// <param name="data">Span containing the floating point values to scale.</param>
    /// <param name="scale">Scale factor applied to each element of <paramref name="data" />.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ScaleInPlace_Vector(Span<float> data, float scale) {
        int vf = Vector<float>.Count;

        // Compute vectorized length with shifts (vf is a power of two today)
        int shift = BitOperations.TrailingZeroCount(vf);
        int vecLen = data.Length >> shift; // number of Vector<float> chunks
        int vectorized = vecLen << shift; // elements covered by SIMD

        if (vecLen > 0) {
            var scaleVec = new Vector<float>(scale);

            // Vector view over the SIMD prefix
            Span<Vector<float>> vspan = MemoryMarshal.Cast<float, Vector<float>>(data[..vectorized]);

            ref Vector<float> r = ref MemoryMarshal.GetReference(vspan);
            nint i = 0, n = vspan.Length;
            IntPtr n2 = n & ~1; // even count for unroll-by-2

            // Unroll x2 (empirically best for throughput/code size)
            for (; i < n2; i += 2) {
                Vector<float> a = Unsafe.Add(ref r, i);
                a *= scaleVec;
                Unsafe.Add(ref r, i) = a;

                Vector<float> b = Unsafe.Add(ref r, i + 1);
                b *= scaleVec;
                Unsafe.Add(ref r, i + 1) = b;
            }

            // Leftover one vector
            if ((n & 1) != 0) {
                Vector<float> c = Unsafe.Add(ref r, i);
                c *= scaleVec;
                Unsafe.Add(ref r, i) = c;
            }
        }

        ScaleInPlace_Scalar(data[vectorized..], scale);
    }

    internal enum IntrinsicBackend {
        /// <summary>
        ///     Use the scalar implementation without SIMD acceleration.
        /// </summary>
        Scalar,

        /// <summary>
        ///     Use the SIMD vectorized implementation when supported by the runtime.
        /// </summary>
        Vector
    }
}