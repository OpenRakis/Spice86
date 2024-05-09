namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Represents a sound channel, which is used to render audio samples.
/// </summary>
public class SoundChannel {
    private readonly SoftwareMixer _mixer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundChannel"/> class.
    /// </summary>
    /// <param name="mixer">The software mixer to register the sound channel with.</param>
    /// <param name="name">The name of the sound channel.</param>
    public SoundChannel(SoftwareMixer mixer, string name) {
        _mixer = mixer;
        Name = name;
        mixer.Register(this);
    }

    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    /// <param name="data">The audio frame to mix and eventually render.</param>
    public int Render(Span<float> data) {
        return _mixer.Render(data, this);
    }
    
    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    /// <param name="data">The audio frame to mix and eventually render.</param>
    public int Render(Span<short> data) {
        return _mixer.Render(data, this);
    }
    
    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    /// <param name="data">The audio frame to mix and eventually render.</param>
    public int Render(Span<byte> data) {
        return _mixer.Render(data, this);
    }

    /// <summary>
    /// Gets or sets the stereo separation, as a percentage.
    /// </summary>
    public float StereoSeparation { get; set; } = 50;

    /// <summary>
    /// Gets the name of the sound channel.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the sound channel is muted.
    /// </summary>
    public bool IsMuted { get; set; }

    private int _volume = 100;

    /// <summary>
    /// Gets or sets the volume, as a percentage.
    /// </summary>
    public int Volume {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }
}
