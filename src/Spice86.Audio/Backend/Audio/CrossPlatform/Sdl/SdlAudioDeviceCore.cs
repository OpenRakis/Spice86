namespace Spice86.Audio.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Core audio buffer fill logic shared across all platform backends.
/// Invokes the user callback and handles silence padding.
/// Reference: SDL_RunAudio callback fill logic (SDL_audio.c lines 720-770)
/// </summary>
internal sealed class SdlAudioDeviceCore {
    /// <summary>
    /// Creates a new core with the given spec and buffer size.
    /// </summary>
    public SdlAudioDeviceCore(AudioSpec spec, int bufferSizeBytes) {
        Spec = spec;
        BufferSizeBytes = bufferSizeBytes;
    }

    /// <summary>
    /// The audio spec for this device.
    /// </summary>
    public AudioSpec Spec { get; }

    /// <summary>
    /// The buffer size in bytes.
    /// </summary>
    public int BufferSizeBytes { get; }

    /// <summary>
    /// Fills the device buffer by invoking the callback.
    /// Reference: SDL_RunAudio callback invocation (line 742)
    /// callback(udata, data, data_len)
    /// </summary>
    public unsafe void FillDeviceBuffer(IntPtr bufferPtr, int bufferBytes) {
        int clampedBytes = Math.Min(bufferBytes, BufferSizeBytes);

        AudioCallback? callback = Spec.Callback;
        if (callback != null) {
            int sampleCount = clampedBytes / sizeof(float);
            Span<float> buffer = new Span<float>(bufferPtr.ToPointer(), sampleCount);
            callback.Invoke(buffer);
        } else {
            NativeMemory.Clear(bufferPtr.ToPointer(), (nuint)clampedBytes);
        }
    }
}
