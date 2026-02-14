namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Linux.Alsa;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// ALSA PCM constants matching alsa/pcm.h.
/// Reference: /usr/include/alsa/pcm.h
/// </summary>
internal static class AlsaConstants {
    // snd_pcm_stream_t
    public const int SndPcmStreamPlayback = 0;
    public const int SndPcmStreamCapture = 1;

    // snd_pcm_access_t
    public const int SndPcmAccessMmapInterleaved = 0;
    public const int SndPcmAccessMmapNoninterleaved = 1;
    public const int SndPcmAccessMmapComplex = 2;
    public const int SndPcmAccessRwInterleaved = 3;
    public const int SndPcmAccessRwNoninterleaved = 4;

    // snd_pcm_format_t
    public const int SndPcmFormatUnknown = -1;
    public const int SndPcmFormatS8 = 0;
    public const int SndPcmFormatU8 = 1;
    public const int SndPcmFormatS16Le = 2;
    public const int SndPcmFormatS16Be = 3;
    public const int SndPcmFormatU16Le = 4;
    public const int SndPcmFormatU16Be = 5;
    public const int SndPcmFormatS24Le = 6;
    public const int SndPcmFormatS24Be = 7;
    public const int SndPcmFormatU24Le = 8;
    public const int SndPcmFormatU24Be = 9;
    public const int SndPcmFormatS32Le = 10;
    public const int SndPcmFormatS32Be = 11;
    public const int SndPcmFormatU32Le = 12;
    public const int SndPcmFormatU32Be = 13;
    public const int SndPcmFormatFloatLe = 14;
    public const int SndPcmFormatFloatBe = 15;
    public const int SndPcmFormatFloat64Le = 16;
    public const int SndPcmFormatFloat64Be = 17;

    // Open mode flags
    public const int SndPcmNonblock = 0x00000001;

    // Error codes
    public const int Eagain = 11;
}

/// <summary>
/// P/Invoke bindings for libasound (ALSA).
/// Reference: SDL_alsa_audio.c function pointer declarations (lines 48-100)
/// All functions match the ALSA API exactly.
/// </summary>
internal static partial class AlsaNativeMethods {
    private const string LibAsound = "libasound.so.2";

    // --- PCM open/close/control ---

    /// <summary>
    /// Opens a PCM device.
    /// Reference: snd_pcm_open
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    /// <summary>
    /// Closes a PCM device.
    /// Reference: snd_pcm_close
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_close(IntPtr pcm);

    /// <summary>
    /// Writes interleaved frames to a PCM device.
    /// Reference: snd_pcm_writei
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong size);

    /// <summary>
    /// Recovers the PCM stream state after an error.
    /// Reference: snd_pcm_recover
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    /// <summary>
    /// Prepares the PCM for use.
    /// Reference: snd_pcm_prepare
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_prepare(IntPtr pcm);

    /// <summary>
    /// Drains the PCM.
    /// Reference: snd_pcm_drain
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_drain(IntPtr pcm);

    /// <summary>
    /// Returns an ALSA error string.
    /// Reference: snd_strerror
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_strerror(int errnum);

    /// <summary>
    /// Gets the error string as a managed string.
    /// </summary>
    public static string GetErrorString(int errnum) {
        IntPtr ptr = snd_strerror(errnum);
        return Marshal.PtrToStringAnsi(ptr) ?? $"Unknown ALSA error {errnum}";
    }

    // --- Hardware parameters ---

    /// <summary>
    /// Allocates hardware params on the heap.
    /// Reference: snd_pcm_hw_params_malloc
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_malloc(out IntPtr ptr);

    /// <summary>
    /// Frees hardware params.
    /// Reference: snd_pcm_hw_params_free
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_hw_params_free(IntPtr ptr);

    /// <summary>
    /// Fills hw params with the full configuration space.
    /// Reference: snd_pcm_hw_params_any
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_any(IntPtr pcm, IntPtr hwparams);

    /// <summary>
    /// Copies hw params.
    /// Reference: snd_pcm_hw_params_copy
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_hw_params_copy(IntPtr dst, IntPtr src);

    /// <summary>
    /// Sets the access type.
    /// Reference: snd_pcm_hw_params_set_access
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_access(IntPtr pcm, IntPtr hwparams, int access);

    /// <summary>
    /// Sets the sample format.
    /// Reference: snd_pcm_hw_params_set_format
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_format(IntPtr pcm, IntPtr hwparams, int format);

    /// <summary>
    /// Sets the number of channels.
    /// Reference: snd_pcm_hw_params_set_channels
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_channels(IntPtr pcm, IntPtr hwparams, uint channels);

    /// <summary>
    /// Gets the number of channels.
    /// Reference: snd_pcm_hw_params_get_channels
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_channels(IntPtr hwparams, out uint channels);

    /// <summary>
    /// Sets the sample rate nearest to the requested value.
    /// Reference: snd_pcm_hw_params_set_rate_near
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_rate_near(IntPtr pcm, IntPtr hwparams, ref uint rate, IntPtr dir);

    /// <summary>
    /// Sets the period size nearest to the requested value.
    /// Reference: snd_pcm_hw_params_set_period_size_near
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_period_size_near(IntPtr pcm, IntPtr hwparams, ref ulong frames, IntPtr dir);

    /// <summary>
    /// Gets the period size.
    /// Reference: snd_pcm_hw_params_get_period_size
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_period_size(IntPtr hwparams, out ulong frames, IntPtr dir);

    /// <summary>
    /// Sets the minimum number of periods.
    /// Reference: snd_pcm_hw_params_set_periods_min
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_periods_min(IntPtr pcm, IntPtr hwparams, ref uint periods, IntPtr dir);

    /// <summary>
    /// Sets the number of periods to the first available value.
    /// Reference: snd_pcm_hw_params_set_periods_first
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_periods_first(IntPtr pcm, IntPtr hwparams, ref uint periods, IntPtr dir);

    /// <summary>
    /// Gets the number of periods.
    /// Reference: snd_pcm_hw_params_get_periods
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_periods(IntPtr hwparams, out uint periods, IntPtr dir);

    /// <summary>
    /// Sets the buffer size nearest to the requested value.
    /// Reference: snd_pcm_hw_params_set_buffer_size_near
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_buffer_size_near(IntPtr pcm, IntPtr hwparams, ref ulong bufferSize);

    /// <summary>
    /// Gets the buffer size.
    /// Reference: snd_pcm_hw_params_get_buffer_size
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_buffer_size(IntPtr hwparams, out ulong bufferSize);

    /// <summary>
    /// Installs the hardware configuration.
    /// Reference: snd_pcm_hw_params
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params(IntPtr pcm, IntPtr hwparams);

    // --- Software parameters ---

    /// <summary>
    /// Allocates software params on the heap.
    /// Reference: snd_pcm_sw_params_malloc
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_malloc(out IntPtr ptr);

    /// <summary>
    /// Frees software params.
    /// Reference: snd_pcm_sw_params_free
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_sw_params_free(IntPtr ptr);

    /// <summary>
    /// Gets the current software params from the device.
    /// Reference: snd_pcm_sw_params_current
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_current(IntPtr pcm, IntPtr swparams);

    /// <summary>
    /// Sets the start threshold.
    /// Reference: snd_pcm_sw_params_set_start_threshold
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_set_start_threshold(IntPtr pcm, IntPtr swparams, ulong threshold);

    /// <summary>
    /// Sets the minimum available frames to consider the device ready.
    /// Reference: snd_pcm_sw_params_set_avail_min
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_set_avail_min(IntPtr pcm, IntPtr swparams, ulong val);

    /// <summary>
    /// Installs the software configuration.
    /// Reference: snd_pcm_sw_params
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params(IntPtr pcm, IntPtr swparams);

    // --- PCM control ---

    /// <summary>
    /// Sets blocking/non-blocking mode.
    /// Reference: snd_pcm_nonblock
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_nonblock(IntPtr pcm, int nonblock);

    /// <summary>
    /// Waits for a PCM to become ready.
    /// Reference: snd_pcm_wait
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_wait(IntPtr pcm, int timeout);

    /// <summary>
    /// Gets the number of frames available for writing.
    /// Reference: snd_pcm_avail
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern long snd_pcm_avail(IntPtr pcm);

    /// <summary>
    /// Resets the PCM position.
    /// Reference: snd_pcm_reset
    /// </summary>
    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_reset(IntPtr pcm);
}
