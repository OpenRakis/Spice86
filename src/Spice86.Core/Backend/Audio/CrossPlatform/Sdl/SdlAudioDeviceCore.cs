namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;

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
    /// </summary>
    public unsafe void FillDeviceBuffer(IntPtr bufferPtr, int bufferBytes) {
        int clampedBytes = Math.Min(bufferBytes, BufferSizeBytes);
        int sampleCount = clampedBytes / sizeof(float);
        Span<float> buffer = new Span<float>(bufferPtr.ToPointer(), sampleCount);
        buffer.Clear();

        AudioCallback? callback = Spec.Callback;
        if (callback != null) {
            callback.Invoke(buffer);
        }

        if (clampedBytes < bufferBytes) {
            Span<byte> tail = new Span<byte>(((byte*)bufferPtr) + clampedBytes, bufferBytes - clampedBytes);
            tail.Clear();
        }
    }
}
