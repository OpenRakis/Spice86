namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows;

using System;
using Spice86.Core.Backend.Audio.CrossPlatform;

internal sealed class SdlAudioDeviceCore {
    public SdlAudioDeviceCore(AudioSpec spec, int bufferSizeBytes) {
        Spec = spec;
        BufferSizeBytes = bufferSizeBytes;
    }

    public AudioSpec Spec { get; }
    public int BufferSizeBytes { get; }

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
