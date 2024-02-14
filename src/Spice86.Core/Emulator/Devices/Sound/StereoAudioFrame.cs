namespace Spice86.Core.Emulator.Devices.Sound;

using System.Numerics;

/// <summary>
/// Represents a piece of stereo audio data
/// </summary>
public struct StereoAudioFrame : IAudioFrame {
    private readonly float[] _store = new float[2];

    public StereoAudioFrame() {
    }

    /// <summary>
    /// Gets or sets the data of the left channel.
    /// </summary>
    public float Left {
        get => _store[0];
        set => _store[0] = value;
    }

    /// <summary>
    /// Gets or sets the data of the right channel.
    /// </summary>
    public float Right {
        get => _store[1];
        set => _store[1] = value;
    }

    /// <summary>
    /// Indexed access to either the left or right channel.
    /// </summary>
    /// <param name="i">The channel index.</param>
    public float this[int i] {
        get => int.IsEvenInteger(i) ? Left : Right;
        set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
    }

    public Span<float> Frame => _store.AsSpan();
}