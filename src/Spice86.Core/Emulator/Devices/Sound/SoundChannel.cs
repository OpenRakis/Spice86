namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;

public class SoundChannel {
    private readonly SoftwareMixer _mixer;

    public SoundChannel(SoftwareMixer mixer, string name) {
        _mixer = mixer;
        Name = name;
        mixer.Register(this);
    }

    public void Render(Span<byte> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }
    
    public void Render(Span<short> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }
    
    public void Render(Span<int> buffer) {
        Span<float> dest = new float[buffer.Length];
        SampleConverter.InternalConvert(buffer, dest);
        _mixer.Render(dest, this);
    }

    public void Render(Span<float> buffer)  {
        _mixer.Render(buffer, this);
    }

    /// <summary>
    /// Gets or sets the stereo separation, as a percentage.
    /// </summary>
    public float StereoSeparation { get; set; } = 50;

    public string Name { get; private set; }
    
    public bool IsMuted { get; set; }

    /// <summary>
    /// Gets or sets the volume, as a percentage.
    /// </summary>
    public int Volume { get; set; } = 100;
}
