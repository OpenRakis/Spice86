namespace Bufdio.Spice86.Bindings.Speex;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Platform Invoke bindings to the Speex resampler ABI.
/// Mirrors DOSBox Staging's Speex resampler usage.
/// Reference: speex_resampler.h from libspeexdsp
/// </summary>
internal static partial class NativeMethods {
    public static IntPtr SpeexResamplerInit(
        uint channels,
        uint inRate,
        uint outRate,
        SpeexResamplerQuality quality,
        out SpeexError error) => _bindings.ResamplerInit(channels, inRate, outRate, (int)quality, out error);

    public static void SpeexResamplerDestroy(IntPtr state) => _bindings.ResamplerDestroy(state);

    public static SpeexError SpeexResamplerProcessFloat(
        IntPtr state,
        uint channelIndex,
        ReadOnlySpan<float> input,
        ref uint inputLength,
        Span<float> output,
        ref uint outputLength) => _bindings.ResamplerProcessFloat(state, channelIndex, input, ref inputLength, output, ref outputLength);

    public static SpeexError SpeexResamplerSetRate(
        IntPtr state,
        uint inRate,
        uint outRate) => _bindings.ResamplerSetRate(state, inRate, outRate);

    public static void SpeexResamplerGetRatio(
        IntPtr state,
        out uint ratioNum,
        out uint ratioDen) => _bindings.ResamplerGetRatio(state, out ratioNum, out ratioDen);

    public static SpeexError SpeexResamplerSkipZeros(IntPtr state) => _bindings.ResamplerSkipZeros(state);

    public static SpeexError SpeexResamplerReset(IntPtr state) => _bindings.ResamplerReset(state);

    private interface INativeBindings {
        IntPtr ResamplerInit(uint channels, uint inRate, uint outRate, int quality, out SpeexError error);
        void ResamplerDestroy(IntPtr state);
        SpeexError ResamplerProcessFloat(IntPtr state, uint channelIndex, ReadOnlySpan<float> input, ref uint inputLength, Span<float> output, ref uint outputLength);
        SpeexError ResamplerSetRate(IntPtr state, uint inRate, uint outRate);
        void ResamplerGetRatio(IntPtr state, out uint ratioNum, out uint ratioDen);
        SpeexError ResamplerSkipZeros(IntPtr state);
        SpeexError ResamplerReset(IntPtr state);
    }

    private static readonly INativeBindings _bindings;

    static NativeMethods() {
        if (OperatingSystem.IsWindows()) {
            _bindings = new Windows();
        } else if (OperatingSystem.IsLinux()) {
            _bindings = new Linux();
        } else if (OperatingSystem.IsMacOS()) {
            _bindings = new MacOS();
        } else {
            throw new PlatformNotSupportedException("Speex resampler is only supported on Windows, Linux, and macOS.");
        }
    }

    public static string GetSpeexLibName() {
        if (OperatingSystem.IsWindows()) {
            return "libspeexdsp.dll";
        } else if (OperatingSystem.IsLinux()) {
            return "libspeexdsp.so.1";
        } else if (OperatingSystem.IsMacOS()) {
            return "libspeexdsp.1.dylib";
        } else {
            throw new PlatformNotSupportedException("Speex resampler is only supported on Windows, Linux, and macOS.");
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed partial class Windows : INativeBindings {
        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_init")]
        private static partial IntPtr speex_resampler_init(uint nb_channels, uint in_rate, uint out_rate, int quality, out SpeexError err);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_destroy")]
        private static partial void speex_resampler_destroy(IntPtr st);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_process_float")]
        private static unsafe partial SpeexError speex_resampler_process_float(
            IntPtr st,
            uint channel_index,
            float* @in,
            ref uint in_len,
            float* @out,
            ref uint out_len);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_set_rate")]
        private static partial SpeexError speex_resampler_set_rate(IntPtr st, uint in_rate, uint out_rate);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_get_ratio")]
        private static partial void speex_resampler_get_ratio(IntPtr st, out uint ratio_num, out uint ratio_den);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_skip_zeros")]
        private static partial SpeexError speex_resampler_skip_zeros(IntPtr st);

        [LibraryImport("libspeexdsp.dll", EntryPoint = "speex_resampler_reset_mem")]
        private static partial SpeexError speex_resampler_reset_mem(IntPtr st);

        public IntPtr ResamplerInit(uint channels, uint inRate, uint outRate, int quality, out SpeexError error) {
            return speex_resampler_init(channels, inRate, outRate, quality, out error);
        }

        public void ResamplerDestroy(IntPtr state) {
            speex_resampler_destroy(state);
        }

        public unsafe SpeexError ResamplerProcessFloat(IntPtr state, uint channelIndex, ReadOnlySpan<float> input, ref uint inputLength, Span<float> output, ref uint outputLength) {
            fixed (float* pInput = input)
            fixed (float* pOutput = output) {
                return speex_resampler_process_float(state, channelIndex, pInput, ref inputLength, pOutput, ref outputLength);
            }
        }

        public SpeexError ResamplerSetRate(IntPtr state, uint inRate, uint outRate) {
            return speex_resampler_set_rate(state, inRate, outRate);
        }

        public void ResamplerGetRatio(IntPtr state, out uint ratioNum, out uint ratioDen) {
            speex_resampler_get_ratio(state, out ratioNum, out ratioDen);
        }

        public SpeexError ResamplerSkipZeros(IntPtr state) {
            return speex_resampler_skip_zeros(state);
        }

        public SpeexError ResamplerReset(IntPtr state) {
            return speex_resampler_reset_mem(state);
        }
    }

    [SupportedOSPlatform("linux")]
    private sealed partial class Linux : INativeBindings {
        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_init")]
        private static partial IntPtr speex_resampler_init(uint nb_channels, uint in_rate, uint out_rate, int quality, out SpeexError err);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_destroy")]
        private static partial void speex_resampler_destroy(IntPtr st);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_process_float")]
        private static unsafe partial SpeexError speex_resampler_process_float(
            IntPtr st,
            uint channel_index,
            float* @in,
            ref uint in_len,
            float* @out,
            ref uint out_len);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_set_rate")]
        private static partial SpeexError speex_resampler_set_rate(IntPtr st, uint in_rate, uint out_rate);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_get_ratio")]
        private static partial void speex_resampler_get_ratio(IntPtr st, out uint ratio_num, out uint ratio_den);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_skip_zeros")]
        private static partial SpeexError speex_resampler_skip_zeros(IntPtr st);

        [LibraryImport("libspeexdsp.so.1", EntryPoint = "speex_resampler_reset_mem")]
        private static partial SpeexError speex_resampler_reset_mem(IntPtr st);

        public IntPtr ResamplerInit(uint channels, uint inRate, uint outRate, int quality, out SpeexError error) {
            return speex_resampler_init(channels, inRate, outRate, quality, out error);
        }

        public void ResamplerDestroy(IntPtr state) {
            speex_resampler_destroy(state);
        }

        public unsafe SpeexError ResamplerProcessFloat(IntPtr state, uint channelIndex, ReadOnlySpan<float> input, ref uint inputLength, Span<float> output, ref uint outputLength) {
            fixed (float* pInput = input)
            fixed (float* pOutput = output) {
                return speex_resampler_process_float(state, channelIndex, pInput, ref inputLength, pOutput, ref outputLength);
            }
        }

        public SpeexError ResamplerSetRate(IntPtr state, uint inRate, uint outRate) {
            return speex_resampler_set_rate(state, inRate, outRate);
        }

        public void ResamplerGetRatio(IntPtr state, out uint ratioNum, out uint ratioDen) {
            speex_resampler_get_ratio(state, out ratioNum, out ratioDen);
        }

        public SpeexError ResamplerSkipZeros(IntPtr state) {
            return speex_resampler_skip_zeros(state);
        }

        public SpeexError ResamplerReset(IntPtr state) {
            return speex_resampler_reset_mem(state);
        }
    }

    [SupportedOSPlatform("macos")]
    private sealed partial class MacOS : INativeBindings {
        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_init")]
        private static partial IntPtr speex_resampler_init(uint nb_channels, uint in_rate, uint out_rate, int quality, out SpeexError err);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_destroy")]
        private static partial void speex_resampler_destroy(IntPtr st);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_process_float")]
        private static unsafe partial SpeexError speex_resampler_process_float(
            IntPtr st,
            uint channel_index,
            float* @in,
            ref uint in_len,
            float* @out,
            ref uint out_len);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_set_rate")]
        private static partial SpeexError speex_resampler_set_rate(IntPtr st, uint in_rate, uint out_rate);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_get_ratio")]
        private static partial void speex_resampler_get_ratio(IntPtr st, out uint ratio_num, out uint ratio_den);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_skip_zeros")]
        private static partial SpeexError speex_resampler_skip_zeros(IntPtr st);

        [LibraryImport("libspeexdsp.1.dylib", EntryPoint = "speex_resampler_reset_mem")]
        private static partial SpeexError speex_resampler_reset_mem(IntPtr st);

        public IntPtr ResamplerInit(uint channels, uint inRate, uint outRate, int quality, out SpeexError error) {
            return speex_resampler_init(channels, inRate, outRate, quality, out error);
        }

        public void ResamplerDestroy(IntPtr state) {
            speex_resampler_destroy(state);
        }

        public unsafe SpeexError ResamplerProcessFloat(IntPtr state, uint channelIndex, ReadOnlySpan<float> input, ref uint inputLength, Span<float> output, ref uint outputLength) {
            fixed (float* pInput = input)
            fixed (float* pOutput = output) {
                return speex_resampler_process_float(state, channelIndex, pInput, ref inputLength, pOutput, ref outputLength);
            }
        }

        public SpeexError ResamplerSetRate(IntPtr state, uint inRate, uint outRate) {
            return speex_resampler_set_rate(state, inRate, outRate);
        }

        public void ResamplerGetRatio(IntPtr state, out uint ratioNum, out uint ratioDen) {
            speex_resampler_get_ratio(state, out ratioNum, out ratioDen);
        }

        public SpeexError ResamplerSkipZeros(IntPtr state) {
            return speex_resampler_skip_zeros(state);
        }

        public SpeexError ResamplerReset(IntPtr state) {
            return speex_resampler_reset_mem(state);
        }
    }
}
