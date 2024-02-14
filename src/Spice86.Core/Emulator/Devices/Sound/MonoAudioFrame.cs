namespace Spice86.Core.Emulator.Devices.Sound;

using System.Numerics;

/// <summary>
/// Represents a monaural audio frame
/// </summary>
public struct MonoAudioFrame : IAudioFrame {
    private readonly float[] _store = new float[1];

    public MonoAudioFrame() {
    }

    public Span<float> Frame => _store.AsSpan();
}