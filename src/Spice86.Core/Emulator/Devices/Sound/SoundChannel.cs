namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;

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
    /// Renders the audio samples from the specified byte buffer.
    /// </summary>
    /// <param name="buffer">The byte buffer containing the audio samples.</param>
    public void Render(Span<byte> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }
    
    /// <summary>
    /// Renders the audio samples from the specified short buffer.
    /// </summary>
    /// <param name="buffer">The short buffer containing the audio samples.</param>
    public void Render(Span<short> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }
    
    /// <summary>
    /// Renders the audio samples from the specified int buffer.
    /// </summary>
    /// <param name="buffer">The int buffer containing the audio samples.</param>
    public void Render(Span<int> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }

    /// <summary>
    /// Renders the audio samples from the specified float buffer.
    /// </summary>
    /// <param name="buffer">The float buffer containing the audio samples.</param>
    public void Render(Span<float> buffer)  {
        _mixer.Render(buffer, this);
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
        set {
            int scaledValue = (int)(value / 255.0 * 100);
            _volume = Math.Clamp(scaledValue, 0, 100);
        }
    }
}
